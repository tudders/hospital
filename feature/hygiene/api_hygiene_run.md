# API hygiene — consolidated run sheet

Merges `api_audit_astra.md` (runtime/behavioural) and `api_audit_claude.md` (contract/OpenAPI surface),
adds a third pass over HTTP semantics that neither audit ran, and orders the result as work.

Every finding below was re-verified against the source before it was written down. Line numbers are
current as of this file.

Baseline both audits agree on: `dotnet build` 0 errors / 146 warnings (all CTJ001, all in
`Alcidion.Api.Tests`), `dotnet test` 142 passed / 0 failed, including 32 SQL tests.

---

## Coverage map

21 distinct findings. The two audits overlap on exactly one.

| Source    | Findings         | Nature                                              | Missed                      |
| --------- | ---------------- | --------------------------------------------------- | --------------------------- |
| astra     | 6                | Runtime: concurrency, production config, live 500s  | The entire contract surface |
| claude    | 11 (+5 advisory) | Contract: OpenAPI output, response types, auth docs | The data-corrupting race    |
| this pass | 5                | HTTP semantics: verbs, cache, idempotency           | —                           |

Shared finding: broken `Location` header on admit (astra #5 = claude #2).

**Why the overlap is so small.** Each audit had an oracle and found what its oracle could express.
claude ran off a framework checklist and the emitted OpenAPI document — a generated document renders
verbs as fact, so it can surface what is _declared_ wrong but never what is _designed_ wrong; note
that its findings 1, 4, 5, 6 and 8 are all "an attribute is missing". astra ran reproductions — but
verb misuse, cache leakage and missing idempotency don't fail, they return 200, so a
reproduction-driven pass is blind to them by construction. Design coherence has no oracle. It needs a
read with judgment, which is the pass neither ran.

Two aggravating factors worth remembering for the next audit:

- **Stated intent was read as specification.** This codebase comments itself unusually well, and both
  auditors largely accepted those comments. `EfAdmissionRepository.cs:46-47` claims "a concurrent
  transfer or discharge blocks here rather than interleaving" — that comment is false, and it sits
  directly above the bug. astra only got through it by testing instead of reading.
- **Absences don't appear in enumerations.** Both audits walked what exists. With no PUT/PATCH/DELETE
  in the API, nothing _looks_ like verb misuse; the missing operations are themselves the finding.

---

## Work, in order

### 1. Discharge/transfer race + transfer idempotency — one fix — **DONE**

**Files:** `backend/src/Alcidion.Admissions/Infrastructure/EfAdmissionRepository.cs:189-224`,
`backend/src/Alcidion.Api/Controllers/AdmissionsController.cs:40-50`

Two defects, one root cause, one fix. This is the only finding in any document that corrupts data.

_The race (astra #1 — confirmed)._ `AdmissionService.DischargeAsync:56` reads the admission, captures
`clock.UtcNow` as T1 via `admission.Discharge(...)`, then calls `UpdateAsync`. Inside:

- `:199` matches on `DischargedAt == null`, **not** on `ConcurrencyVersion`. A transfer committing
  between T1 and this statement bumps the version and leaves `DischargedAt` null, so the discharge
  still matches and proceeds on a stale T1.
- `:213` closes bed stays `WHERE StartedAt <= dischargedAt`. The transfer's new stay has
  `StartedAt = T2 > T1` and is excluded.
- `:212` **ignores its row count.** Compare `TransferAsync:67`, which checks `closed == 0` and rolls
  back — transfer-vs-transfer fails safe, discharge-vs-transfer fails open.

Result: admission closes, bed stay stays open, the bed never returns to the pool. Reproduced by astra
against SQL Server.

_The idempotency gap (this pass)._ `POST` means a retry is a second operation. `TransferAsync:48` only
requires the admission be open, so a replayed transfer closes the stay it just created, allocates a
second bed and writes a duplicate `bed_request` — returning 200 over a corrupt bed history. Discharge
retry at least fails closed with 409. `PatientFlow.tsx:214` disables the button while busy, which
covers the double-click and nothing else: not a network retry, not a proxy replay.

**Fix.** Model both transitions as `PATCH /api/admissions/{id}` carrying the state change, gated on
`If-Match` against `ConcurrencyVersion`. The version check closes the lost-update window and makes the
retry safe in the same change. Also add the missing row-count check on the `BedStays` update
regardless of which shape is chosen.

If the action sub-resources are kept instead — defensible, Stripe and GitHub both do it — then an
`Idempotency-Key` header is the alternative, and it is more work for less.

**Test first.** `tests/Alcidion.Sql.Tests/AdmissionRepositoryTests.cs` has no transfer/discharge
interleaving case. Write it before touching the repository.

### 2. Cache directives are inverted

**Files:** `backend/src/Alcidion.Api/Controllers/HospitalOccupancyController.cs:9`,
`backend/src/Alcidion.Api/Controllers/ApiController.cs`

`[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` appears exactly once in the
codebase — on the one controller whose own class comment states it emits "occupancy counts only - no
patient identifiers or demographics". Meanwhile `GET /api/patients/{id}` returns MRN, given name,
family name and date of birth with no cache directive at all, as do `/api/patients`, `/api/wards`,
`/api/admissions` and `/api/auth/me`.

GET is cacheable by default. The protected endpoint got the protection; the PHI endpoints did not.

**Fix.** Move the attribute to `ApiController` so every endpoint inherits it; delete the one-off.

### 3. Telemetry ingest

**Files:** `backend/src/Alcidion.Api.Contracts/ClientEventBatch.cs:69`,
`backend/src/Alcidion.Api/Controllers/TelemetryController.cs`

astra and claude each found one half of this endpoint.

- _The crash (astra #4)._ `(long)e.Seq` and `(long)e.T` throw on a JSON number above `Int64.MaxValue`;
  the schema bounds neither. Sending `9223372036854775808` reproduced a 500.
- _The design (claude #7)._ `[AllowAnonymous]`, no rate limit, no size cap beyond Kestrel's 30 MB
  default, and caller-supplied `props` logged verbatim at Information. The batch caps at 500 items but
  nothing bounds `props`. In a clinical frontend that is an anonymous path for arbitrary caller
  content — plausibly PHI — into the log pipeline, plus a cheap log-volume DoS.

**Fix.** Schema upper bounds on `seq`/`t` matching `long`; `maxLength`/`maxProperties` on `props`;
`[RequestSizeLimit]`; fixed-window rate limiter keyed by session. Also make the 202 body a sealed
record (claude #8) — it is currently an anonymous type and publishes no schema.

### 4. Startup guards — one block covers three findings

**Files:** `backend/src/Alcidion.Api/Program.cs:22-27,42-45`,
`backend/src/Alcidion.Api/appsettings.json`

- _Committed signing key (claude #9)._ `"dev-only-secret-change-me-in-production-0123456789"` sits in
  the base `appsettings.json`, not `appsettings.Development.json`. `JwtOptions.Secret` defaults to
  `""`, so an empty override builds a zero-length HMAC key rather than failing.
- _Demo credentials (astra #2)._ `DevTokenIssuer` is registered unconditionally at `Program.cs:45`.
  Nothing gates on environment; `admin`/`admin` returns an administrator token under Production.
- _Silent in-memory storage (astra #3)._ `Program.cs:22-27` falls back to in-memory repositories when
  the connection string is absent. Production starts and accepts patient and admission writes that
  vanish on restart.

**Severity note.** astra labelled #2 and #3 "High for production". Both are accurate as code facts, but
`DevTokenIssuer`'s own XML comment documents it as standing in for an IdP and `PatientsModule`
documents the in-memory fallback as deliberate — it is what keeps the integration tests hermetic.
These are missing fail-fast guards in a demo app, not live production holes. Fix them; don't panic.

**Fix.** Move the secret to `appsettings.Development.json`. Add one
`if (!builder.Environment.IsDevelopment())` block that throws when `Jwt:Secret` is empty, when the demo
issuer would be registered, and when the connection string is absent.

### 5. Resource model and status codes

- ~~**`GET /api/admissions/{id}` is missing** (astra #5 = claude #2).~~ **Done in item 1.** A
  conditional `PATCH` needs a resource to read the ETag from, so this could not wait: the route is
  `AdmissionsController.Get`, and `Admit` now uses `CreatedAtAction(nameof(Get), ...)` rather than a
  hand-built path. The 201 points at something that answers 200, with the same ETag.
- **`POST /api/admissions` advertises 404** (`AdmissionsController.cs:29`). The 404 refers to the
  patient named in the _body_, not the posted URL; a client reasonably reads 404-on-POST as "no such
  endpoint". Use 422, or a 400 naming `patientId`.
- **PHI in the query string.** `GET /api/patients?search=` and the frontend puts the typed patient name
  straight into it (`PatientFlow.tsx:50`). Query strings reach access logs, proxy logs, browser
  history and `Referer`. This is the standard GET-vs-POST search tradeoff and wants a recorded
  decision, not a default.
- **No PUT/PATCH/DELETE on patients.** Item 1 added the first `PATCH` in the codebase, on
  admissions. Patient records are still write-once: register and read, never correct. An MRN typo or
  a legal name change has no path through this API. Not a bug — a gap in the resource model, and the
  reason verb usage never came under scrutiny.

### 6. OpenAPI contract batch

All of claude's remaining findings. Individually cheap, zero runtime consequence, do them in one
sitting.

- **#1 `GET /api/patients/{id}` publishes no 200.** `PatientsController.cs:30` declares only
  `[ProducesResponseType(404)]`; an explicit declaration replaces MVC's inference, so the success case
  vanished from the document. Generated clients have no return type for fetching a patient. Add
  `[ProducesResponseType<PatientDto>(200)]`.
- **#3 No security scheme in the document.** Every controller bar auth/telemetry is `[Authorize]` and
  the document says nothing — no `components.securitySchemes`, no per-operation security, no 401/403
  outside login. Swagger UI cannot authenticate; generated clients don't know to send a bearer token.
  Needs an `IOpenApiDocumentTransformer` for the bearer scheme plus an operation transformer reading
  the authorize metadata.
- **#4 Every response advertises `text/plain`.** Default formatter set leaking into the contract. One
  `[Produces("application/json")]` on `ApiController` fixes all of them.
- **#5 XML doc comments produce nothing.** No project sets `GenerateDocumentationFile` (verified: no
  `.csproj` or `.props` sets it). Request schemas carry rich descriptions from the JSON Schema
  documents while `WardDto`, `PatientDto`, `AdmissionDto`, `MeResponse` and `HospitalSnapshot` publish
  bare types. The `<summary>` on `WardsController` and the `<param name="FreeBeds">` note on `WardDto`
  are dead text. Set the property, silence CS1591 if you don't want it enforced everywhere.
- **#6 No operation metadata.** Zero summaries, descriptions or operation IDs across 13 operations.
  `[EndpointSummary]`/`[EndpointDescription]` are the controller-side equivalents.
- **#10 No `ValidAlgorithms` restriction** (`Program.cs:48-54`). Pin `["HS256"]`.
- **#11 No `UseHttpsRedirection()` and no HSTS.** Defensible behind a terminating proxy — make it a
  deliberate decision rather than an omission.

### 7. Housekeeping

- **Correlation IDs are lost on 500s (astra #6).** `Program.cs:88` registers `UseExceptionHandler()`
  _before_ `UseMiddleware<CorrelationIdMiddleware>()` at `:90`, so an exception unwinding to the
  handler has already left the correlation middleware — the header is gone and its logging scope has
  ended. Move the correlation middleware above the exception handler.
- **146 CTJ001 warnings**, all in `Alcidion.Api.Tests`, all "use a UTF-8 literal". Fix them or
  `<NoWarn>CTJ001</NoWarn>` in the test csproj. A warning list nobody intends to act on trains you to
  ignore the list.
- **No `.http` file** anywhere in the backend (verified). 13 endpoints, four demo logins and a JWT
  flow, and nothing in the repo shows how to drive them by hand. Worth adding.

---

## Deliberately not doing

Carried from claude's advisory section, with agreement:

- **Service interfaces.** `PatientService` and `AdmissionService` are concrete and injected directly.
  Repositories and the event bus are already behind interfaces and the domain tests exercise the
  services against in-memory stores; a one-implementation interface buys a file and nothing else.
- **`Middleware/` folder.** `CorrelationIdMiddleware` lives in `Observability/`, which reads better.
- **`app.MapGet("/health")` as a minimal API in a controller project.** One line for a liveness probe,
  reads fine. The more useful observation is that there is no `AddHealthChecks()` and the endpoint
  reports `"ok"` without checking the database — that is the thing to fix, if anything.
- **`JsonStringEnumConverter`.** No enum currently reaches the wire; `AdmissionDto:14` stringifies
  `Status` by hand. Latent: the first enum-typed DTO property serialises as an integer. Configure it
  now or keep stringifying by hand deliberately — but decide.

---

## Verification

Before starting: `dotnet build` and `dotnet test` to reconfirm the 142/0 baseline.

Per item:

1. ~~New SQL test asserting no open `bed_stays` row survives a discharge interleaved with a transfer,
   plus a replayed-transfer test asserting exactly one open stay and one `bed_request`.~~ Both written
   and passing — see the progress tracker.
2. Assert `Cache-Control: no-store` on a response from each authenticated GET.
3. Post `seq: 9223372036854775808` and expect 400, not 500. Post an oversized `props` and expect 400.
4. Boot with `ASPNETCORE_ENVIRONMENT=Production` and no connection string — expect a startup throw,
   not a running API.
5. Follow the `Location` header from a successful admit and expect 200.
6. Fetch `/openapi/v1.json` from a live instance and diff against the current document.
7. Force a 500 and assert `X-Correlation-Id` is present on the response.

Close out by confirming the whole suite still passes and the app runs in the browser.

# Progress Tracker

Baseline reconfirmed before starting: `dotnet build` 0 errors, `dotnet test` **142 passed / 0 failed**
(28 domain, 82 API, 32 SQL). The 146 CTJ001 warnings are now 73 — same warnings, counted once per
build rather than twice; item 7 still stands.

## Item 1 — discharge/transfer race + transfer idempotency — DONE

**Suite after: 152 passed / 0 failed** (28 domain, 90 API, 34 SQL). Ten new tests, no test deleted.

### The reproduction, written first

`A_discharge_overtaken_by_a_transfer_loses_rather_than_stranding_the_bed`
(`tests/Alcidion.Sql.Tests/AdmissionRepositoryTests.cs`) failed against the old code exactly as astra
described — `UpdateAsync` returned `true`, the admission closed, and the bed stay stayed open:

```
Assert.False() Failure
Expected: False
Actual:   True
```

### What changed

The version is now the thing both transitions are taken against, end to end.

| File | Change |
| --- | --- |
| `Domain/Admission.cs` | `Version`, carried from storage through `Rehydrate`; `Committed()` is internal, so only a store moves it. |
| `Domain/IAdmissionRepository.cs` | `TransferAsync` takes `long? expectedVersion`; new `TransferResult.VersionMismatch(long CurrentVersion)`. |
| `Infrastructure/EfAdmissionRepository.cs` | Discharge matches on `ConcurrencyVersion == admission.Version`, not just `DischargedAt == null`. Transfer matches the caller's version when given. **The `BedStays` update now checks its row count and rolls the whole discharge back at 0** — the missing check astra called out. `WhyNotTakenAsync` separates "gone or closed" from "moved on". |
| `Infrastructure/InMemoryAdmissionRepository.cs` | The same two checks, so the hermetic tests exercise the same rule. |
| `Application/AdmissionService.cs` | `expectedVersion` on discharge and transfer; `GetAsync`; a losing discharge re-reads to say whether it was closed or moved. |
| `Shared/Result.cs` | `Error.PreconditionFailed` → 412 in `ApiController`. A stale version is not a conflict: nothing about the request is wrong. |

The false comment at `EfAdmissionRepository.cs:46-47` — "a concurrent transfer or discharge blocks
here" — is gone. It now says what the code does.

### The HTTP shape

`POST /{id}/transfer` and `POST /{id}/discharge` are replaced by `PATCH /api/admissions/{id}`, gated
on `If-Match`. Both were removed rather than kept alongside: leaving the unguarded verb in place
would leave the corruption path open, which is the whole point of the item.

- `{"ward": "..."}` transfers, `{"status": "discharged"}` discharges. Exactly one, enforced by
  `minProperties`/`maxProperties`/`additionalProperties: false` in
  `Schemas/change-admission-request.json` — so the action never has to guess which a caller meant.
- `If-Match` is **required**. No header → 428. `*` or a weak tag → 428, because neither names a
  version. Stale → 412 naming the current version. `AdmissionDto` carries `version` and every
  response carries the matching `ETag`.
- Audit granularity survives the merge: `AuditedAttribute` now prefers a name the action sets, so the
  trail still reads `AUDIT patient.discharge` and `AUDIT patient.transfer`, not the endpoint's name.
- Two keywords the new schema uses had to be taught to the publishers: `enum`, `minProperties` and
  `maxProperties` in `JsonSchemaOpenApiTransformer`, and `enum` in the frontend's
  `schemas-to-ts.mjs` — which now emits `status?: "discharged"` rather than `status?: string`.

### Frontend

`src/lib/admissions.ts` is the one place a change is sent; `PatientFlow.tsx` and `Admissions.tsx`
both go through it. A 412 refreshes the board before showing the error, so a retry is decided
against what the admission became rather than against what it was.

### Tests added

SQL (`AdmissionRepositoryTests`):

- `A_discharge_overtaken_by_a_transfer_loses_rather_than_stranding_the_bed` — the reproduction above.
- `A_replayed_transfer_claims_one_bed_not_two` — destination seeded with two free beds on purpose, so
  what stops the second claim is the check and not the ward being full.

API (`AdmissionChangeTests`, new file): Location resolves; no `If-Match` → 428; weak tag → 428; stale
→ 412; replayed transfer moves the patient once; a change advances the published version; a body
naming both a ward and a status → 400; a discharge is audited as `patient.discharge`.

### Verified against the running app

Against the SQL-backed API on :5025, the full ladder: 201 with `ETag: "0"` and a `Location` that
answers 200 · no `If-Match` → 428 · `If-Match: *` → 428 · `W/"0"` → 428 · both fields → 400 ·
transfer at `"0"` → 200, version 1 · **replay at `"0"` → 412** · stale discharge at `"0"` → 412 ·
discharge at `"1"` → 200, version 2 · discharge again at `"2"` → 409 "already discharged".

In the browser (admit → transfer → discharge through Patient flow) both writes went out as
`PATCH ... => 200`, no console errors, and every ward returned to its starting free-bed count —
ICU 6, Ward 12 7, General Medicine 6, Emergency Department 4. Nothing stranded, which is the
symptom the whole item exists to prevent.

### Not done, deliberately

- `Idempotency-Key` was the alternative if the action sub-resources had been kept. They were not, so
  it is moot.
- The 400 for a body naming both fields reports `"": ["The value was expected to match the
  subschema."]`. Vague, but it is a schema-level failure with no single field to blame, and the
  frontend's union type makes the body unwriteable there. Left as is.

## Next

Item 2 (cache directives) is unstarted.
