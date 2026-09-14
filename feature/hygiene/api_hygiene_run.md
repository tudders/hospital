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

### 2. Cache directives are inverted — **DONE**

**Files:** `backend/src/Alcidion.Api/Controllers/HospitalOccupancyController.cs:9`,
`backend/src/Alcidion.Api/Controllers/ApiController.cs`

`[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` appears exactly once in the
codebase — on the one controller whose own class comment states it emits "occupancy counts only - no
patient identifiers or demographics". Meanwhile `GET /api/patients/{id}` returns MRN, given name,
family name and date of birth with no cache directive at all, as do `/api/patients`, `/api/wards`,
`/api/admissions` and `/api/auth/me`.

GET is cacheable by default. The protected endpoint got the protection; the PHI endpoints did not.

**Fix.** Move the attribute to `ApiController` so every endpoint inherits it; delete the one-off.

### 3. Telemetry ingest — **DONE**

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

### 4. Startup guards — one block covers three findings — **DONE**

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

### 5. Resource model and status codes — **DONE**

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

### 6. OpenAPI contract batch — **DONE**

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

## Item 2 — shared cache directives — DONE

Moved `[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]` to
`ApiController` and removed the occupancy controller's duplicate. Derived controllers now inherit
`Cache-Control: no-store,no-cache`, including patient details and `/api/auth/me`.

Added six HTTP regression cases in `tests/Alcidion.Api.Tests/CacheControlTests.cs`: patient and
admission lists/details, wards, and identity. All six failed first because the cache header was
absent, then passed after the move. The existing occupancy no-store assertion also remains green.

Verification:

- Baseline: **152 passed / 0 failed** (28 domain, 90 API, 34 SQL).
- After: **158 passed / 0 failed** (28 domain, 96 API, 34 SQL), no skipped tests.
- Used Release builds because the running Debug API held its DLLs open; Release built with zero
  errors and the existing 73 CTJ001 warnings.
- Ran the updated SQL-backed API separately on :5026. All seven authenticated GET routes returned
  200 with `Cache-Control: no-store,no-cache`, including successful patient/admission detail and
  hospital occupancy responses.
- Browser smoke check against the updated API: sign-in, hospital overview, and patient flow loaded
  without console errors. Browser requests were routed to :5026 for this check.

## Item 3 — telemetry ingest — DONE

**Suite after: 170 passed / 0 failed** backend (28 domain, 108 API, 34 SQL) + **17 passed / 0 failed**
frontend. The backend half of this item had already landed in `475ef36`; the tracker line calling it
unstarted was stale. What this session added is the half that had not been checked: whether the app
can still talk to the endpoint it just tightened.

### Already in place, re-verified

Every fix the item asked for was present and green:

| Fix | Where |
| --- | --- |
| Schema bounds on `seq`/`t` | `Schemas/client-event-batch.json` — `maximum: 9223372036854775807` on both |
| `maxLength`/`maxProperties` on `props` | same file — 32 properties, 1024-character strings, scalars only |
| `[RequestSizeLimit]` | `TelemetryController.cs:13` — 64 KiB, with a resource filter that also rejects a known-oversized body before reading it and preserves the 413 a chunked body earns during binding |
| Rate limiter keyed by session | `Observability/TelemetryIngestFilter.cs` — partitioned fixed window, 30/min, `Retry-After` |
| Sealed 202 record (claude #8) | `Alcidion.Api.Contracts/TelemetryAcceptedResponse.cs` |

It also closed something neither audit named: a batch mixing session ids is a 400, so one session
cannot charge another's quota.

Live against the API on :5027 — `seq`/`t` at 2^63 → 400 naming `[0].seq`/`[0].t` · `Int64.MaxValue`
→ 202 and the exact value in the log · a 1025-character prop, a 33rd property, a nested object and
an array → 400 · a mixed-session batch → 400 naming `[1].sessionId` · a 64 KiB body → 413 · 30
batches accepted then 429 with `Retry-After: 60`, another session unaffected. No rejected shape
reached the log pipeline, and no 500s.

### The regression the browser found

Every one of those checks passed while the app itself was **losing all of its telemetry**. On the
first browser run the login batch came back:

```
400  {"[5].props.roles":["The value was expected to be of type '[\"null\",\"boolean\",\"number\",\"string\"]'"]}
```

`Login.tsx:33` sent `roles: me.roles` — a `string[]`, against a `props` that item 3 had just
narrowed to flat scalars. The API validates the batch as a unit, so one bad value cost all nine
events, and `flush()` swallows the failure by design ("telemetry must never break the app"). Every
signed-in session lost its start, its login and its first API calls, silently, and in production
nothing would ever have said so.

The tests did not catch it because they only ever asserted that an array **is** rejected. Nothing
asserted the app stays inside the bounds the endpoint now enforces.

### Why the type system did not catch it either

`props` generated as `Record<string, unknown>`. `schemas-to-ts.mjs` handled `additionalProperties`
only as the `false` that closes an object; where it carries a *schema for the values*, that schema
was dropped. It could not have been read anyway — the value's `type` is a list, and the generator's
switch had no case for one.

Both are now taught, the same way `enum`, `minProperties` and `maxProperties` were in item 1:

- a list-valued `type` becomes a union, with `null` mapping to `null`;
- an object with no `properties` but an `additionalProperties` schema becomes `Record<string, T>`.

Regenerating changed exactly one line of `contracts.ts`:

```
- props?: Record<string, unknown>
+ props?: Record<string, string | number | boolean | null>
```

### What changed

| File | Change |
| --- | --- |
| `scripts/schemas-to-ts.mjs` | The two keywords above. |
| `src/lib/contracts.ts` | Regenerated — one line. |
| `src/lib/telemetry.ts` | `track` takes the generated `props` type, exported as `EventProps`. `bounded()` clips string values to 1024 characters on the way into the queue. |
| `src/lib/redaction.ts` | `describeField` returns its actual shape rather than `Record<string, unknown>`. |
| `src/lib/session-recorder.ts` | `markSessionStartOnce` takes `EventProps`. |
| `src/components/Login.tsx` | `roles: me.roles.join(' ')` — the bug. |

Types cannot police length, and the unbounded values are the interesting ones: an `app.error`
message, a `String(e.reason)` from a rejected promise. Those are the next batch to be lost this way,
so `bounded()` clips rather than letting one of them take the batch around it down.

### Proof the guard works

Re-introducing the exact bug fails the build, which is what `npm run build` runs:

```
src/components/Login.tsx(35,44): error TS2322:
  Type 'string[]' is not assignable to type 'string | number | boolean | null'.
```

Restored, `tsc -b` exits 0, `oxlint` is clean and the bundle is inside budget (78.38 kB of 90 kB).

### Tests added

`src/lib/telemetry.test.ts`, new `props stay inside what the API accepts` block: an overlong string
is clipped to 1024; a value at the limit is left alone and its props object is not copied; queued
props are flat scalars within length, written around the `auth.login` shape that broke.

One existing test moved: the buffer-ceiling case fills the shared queue to `MAX_QUEUE`, so anything
tracked after it is dropped. It is now last in the file, in its own block, saying so.

### Verified in the browser

Signed in against the SQL-backed API on :5027 with a cleared session: telemetry `202`, **zero**
console errors, and the batch that used to fail now in the log as

```
CLIENT auth.login session=dcc3a8f0-… props={"user":"nurse","roles":"clinician"}
```

The recorder path is intact — `ui.change` logged
`{"field":"patient-flow/input","inputType":"input","valueLength":5,"filled":true}`, the typed value
itself never leaving the browser. Hospital overview, the 3D occupancy model and patient flow all
rendered. Across the whole session: no schema failure, no exception.

### Noted, not changed

- **One bad value fails up to 500 events.** Correct for a typed contract, expensive for a
  best-effort telemetry stream, and invisible because the client drops the 400. Either the endpoint
  accepts the batch and reports per-item rejections in the 202, or the client reports what it lost.
  Worth a decision; it is not item 3's.
- `[AllowAnonymous]` stays. The rate limit and size cap were the answer to it, per the item.

## Item 4 — startup guards — DONE

**Suite after: 182 passed / 0 failed** (28 domain, 120 API, 34 SQL). Twelve new tests, none deleted.

### The shape

All three findings are the same finding: something that works by default in development keeps
working, silently, where it must not. So they are one guard, `Configuration/StartupGuards.cs`,
called from `Program.cs:55` before `builder.Build()`.

| File | Change |
| --- | --- |
| `appsettings.json` → `appsettings.Development.json` | `Jwt:Secret` moved. The base file no longer ships a working signing key. |
| `Configuration/StartupGuards.cs` | New. `Check` returns the failures, `Verify` throws them as one message. |
| `Auth/DemoUsers.cs` | New. `Auth:AllowDemoUsers`, default true, injected as a singleton. |
| `Auth/DevTokenIssuer.cs` | `Issue` returns null when demo users are off — the credentials are a constant in this assembly, so the check has to stand between them and a signature. |
| `Controllers/AuthController.cs` | 501 when demo users are off. |
| `Program.cs` | Reads the flag, calls the guard, registers `DemoUsers`. |

Three decisions worth recording:

- **Testing is exempt alongside Development.** `ApiFixture` boots this same `Program` with no
  database and signs in as the demo users; a hermetic test run _is_ the scaffolding working. Named
  once, in `StartupGuards.AllowsDemoScaffolding`, rather than spelled `IsDevelopment()` at each site.
- **Every failure is reported at once.** A guard that throws on the first problem produces three
  deployments that each got one step further. The message names the environment variable that
  settles each one.
- **The secret is checked for length, not just emptiness.** `JwtOptions.Secret` defaults to `""` and
  `SymmetricSecurityKey` takes a zero-length key without complaint, but so does a 12-character one —
  and HS256 then throws at the first login, which reads as a runtime fault rather than a config
  error. 32 bytes, checked at startup.

`Auth:AllowDemoUsers=false` is the reason the guard can be satisfied at all: without an opt-out the
API could never start outside development, which would make the other two checks unreachable.
Turning it off leaves no issuer behind `POST /api/auth/login`, so it answers **501**, not 401 — 401
reads as "wrong password" and sends the caller looking for a better one.

### Tests added

`tests/Alcidion.Api.Tests/StartupGuardTests.cs`: Development and Testing pass with nothing
configured; a configured Production passes; each of the three failures is reported on its own and
names its environment variable; a 31-byte key is refused; a blank connection string counts as
missing; all three are reported together; `Verify` names the environment. Then two that boot the
real `Program` — Production with nothing configured does not start, and with the demo users off
login answers 501.

`ApiFixture` now supplies its own `Jwt:Secret` via `UseSetting`. It has to be `UseSetting`: under
minimal hosting `ConfigureAppConfiguration` runs after `Program` has already read its configuration,
so the value arrives too late to sign with — which showed up as all 108 existing API tests failing
with a zero-length key.

### Verified against the running app

`ASPNETCORE_ENVIRONMENT=Production` on the built API, nothing else set:

```
Unhandled exception. System.InvalidOperationException: The API cannot start in the Production environment:
  - Jwt:Secret is 0 bytes and HS256 needs at least 32. Set Jwt__Secret; an unset one signs every token with a zero-length key.
  - The demo token issuer is enabled, so admin/admin returns an administrator token. Set Auth__AllowDemoUsers=false and put a real identity provider in front of this API.
  - No hospital database is configured, so every domain would fall back to its in-memory repository and lose all patient and admission writes on restart. Set ConnectionStrings__Hospital.
```

The same binary with `Jwt__Secret`, `Auth__AllowDemoUsers=false` and `ConnectionStrings__Hospital`
supplied starts, answers `/health` 200, and answers `admin`/`admin` with **501**, not a token.

In the browser against the Development API on :5025: sign-in 200, `/api/auth/me` 200, hospital
overview and patient flow both render, telemetry 202, **zero console errors**. Item 3's regression
has not come back.

### Also changed

`backend/README.md` and `backend/.agents/AGENTS.md` both said the dev key lives in
`appsettings.json`. Both now say where it is and what a deployment has to supply; AGENTS gains rule
11, that development defaults stop at the environment boundary.

## Item 5 — resource model and status codes — DONE

**Suite after: 231 passed / 0 failed** backend (46 domain, 144 API, 41 SQL) + **24 passed / 0 failed**
frontend. Forty-nine new backend tests and seven new frontend ones; none deleted, four rewritten
where they pinned a status code this item deliberately changed.

### The 404 on `POST /api/admissions` is a 422

`Error.UnprocessableReference(field, what, id)` is a new code alongside `NotFound`, carrying the
request field it is about. `ApiController` maps it to **422** with the same `errors` dictionary a
schema failure uses, so the frontend's existing `fieldErrors()` path reads it without changing.

The distinction it draws is the whole point: `not_found` is about the URL, and a 404 answering a POST
tells a client the endpoint is wrong — which sends them looking for the wrong problem. The collection
is there; what is missing is the patient the *body* names, so the failure names `patientId`.

`AdmitSchemaValidationTests.An_unknown_patient_is_still_the_services_404` and
`PatientFlowTests.Admit_unknown_patient_is_404_problem_details` were rewritten rather than deleted:
both pinned the behaviour this item exists to change, and both now also assert the field.

### PHI in the query string — decided, and the real leak closed

**`docs/adr/0004-patient-search-stays-a-get.md`.** The search stays a GET. `POST
/api/patients/searches` would misreport every read as a write in the audit trail, the metrics and any
proxy rule that classifies by method, and would move the name from a query string into a body the
same logs can be configured to capture. Not "GET leaks and POST doesn't" — "GET leaks by default into
two systems we run, POST costs correctness in three we also run."

Checking the four usually-named copies first is what made the decision cheap, because **two of them
do not apply here**:

| Copy | Applies? | Why |
| --- | --- | --- |
| Browser history | No | The search is a `fetch`, not a navigation. The fetched URL never enters history. |
| `Referer` | No | A fetch sends the *page* URL, not the URL being fetched, and the API answers JSON. |
| Server/proxy access logs | Yes | IIS writes `cs-uri-query` by default, and IIS is the deploy target. |
| OpenTelemetry spans | **Yes, and neither audit named it** | `url.query` is set verbatim on every server span. With an OTLP endpoint configured that is a patient's name leaving the building. |

`Observability/QueryRedaction.cs` replaces the value of every sensitive query parameter with
`REDACTED` through `EnrichWithHttpRequest`, which runs after the instrumentation has set the tag. The
parameter *name* is kept — dropping the query wholesale would cost the one thing the attribute is
read for, telling a search apart from a list, and a redaction that costs its own reason gets switched
off again. The proxy's log is the half only a deployment can close, so `backend/README.md` carries it.

Verified live: a search for `Lovelace` produced `url.query: ?search=REDACTED` on the span, and the
string `Lovelace` appears **zero** times in the whole API log — for a patient whose family name it is.

### `PATCH /api/patients/{id}` — the write-once record

The gap the run sheet named: register and read, never correct. An MRN typed wrong or a legal name
change had no path through the API at all.

**`database/004_patient_demographic_corrections.sql`** adds `concurrency_version` to `dbo.patients`,
the column `dbo.admissions` already carries. It goes in first because correcting a patient is a
lost-update problem the moment two people can do it, and demographics are exactly what two people
correct at once — a clerk fixing the MRN while a nurse fixes the spelling of a name.

| File | Change |
| --- | --- |
| `Domain/Patient.cs` | `Version`, carried from storage through `Rehydrate`; `Committed()` internal, so only a store moves it. `Correct(...)` takes a nullable per field — null means "leave this alone" — and **validates everything before writing anything**, so a body that fails on its last field cannot leave the aggregate half-corrected. |
| `Domain/IPatientRepository.cs` | `TryCorrectAsync(corrected, expectedVersion)`; new `CorrectionResult` with `Corrected`, `NotFound`, `VersionMismatch(current)`, `MrnTaken`. |
| `Infrastructure/EfPatientRepository.cs` | One conditional `ExecuteUpdate` decides both questions at once: the version in the `WHERE`, and MRN uniqueness through `ux_patients_mrn`. `IsDuplicateKey` now matches the bare `SqlException` too — `SaveChanges` wraps the provider exception, `ExecuteUpdate` runs its own command and lets it through. |
| `Infrastructure/InMemoryPatientRepository.cs` | The same two checks — and **reads now hand out copies**. Sharing the stored instance meant a correction that loses on its version had already mutated the store on its way to being refused: a failure the SQL store cannot have, so not one the hermetic tests may be written against. |
| `Application/PatientService.cs` | `CorrectAsync`, publishing `PatientCorrected`. |
| `Alcidion.Patients.Contracts/PatientCorrected.cs` | New event. Deliberately not a second `PatientRegistered`: a subscriber that learns a new patient and one that revises a patient it holds are not the same handler, and replaying a stream of "registered" would register them again. |
| `Alcidion.Admissions/Application/PatientCorrectedHandler.cs` | Keeps Admissions' read model current. Without it the in-memory copy keeps the misspelling for good — nothing else ever revisits it. `EfKnownPatients` projects the row directly and so has nothing to do, the same asymmetry `PatientRegisteredHandler` already lives with. |

**The HTTP shape.** `If-Match` required (428 without, 428 for `*` or a weak tag, 412 when stale naming
the current version), `PatientDto` carries `version`, every response carries the matching `ETag`, and
`Register` now emits one too. `ExpectedVersion()`, `PreconditionRequired()` and `PublishVersion()`
moved up to `ApiController`, because item 1 had written them once already and this was the second.

PATCH rather than PUT: a correction is partial by nature. A clerk fixing an MRN has no business
restating a date of birth they did not check, and a PUT that omitted it would either erase it or
quietly mean PATCH anyway. `minProperties: 1` and `additionalProperties: false` mean an empty body and
a body naming `registeredAt` are both 400s.

**No DELETE, deliberately.** A patient with admissions, bed stays and an audit trail behind them
cannot be removed without taking the record of their care with it. A record entered in error is a
correction or a merge, neither of which is what DELETE means. Recorded on the action.

### What the browser found: two dead components

`PatientFlow.tsx` is the only patient screen `App.tsx` renders. **`Patients.tsx` and `Admissions.tsx`
are imported by nothing.** The correction UI was built in `Patients.tsx` first and would never have
been seen; it was reverted and rebuilt in the patient record modal in `PatientFlow.tsx`, where it is
now reachable as **Correct details**.

This is worth more than the fix. Item 1's tracker records editing `Admissions.tsx` as part of moving
transfer and discharge onto `PATCH` — the orphans have been carried through earlier items as though
they were live. They are not deleted here, because deleting files is not this item's call, but they
should go.

`TransferPatientRequest.cs` and `transfer-patient-request.json` were dead the same way — item 1
replaced the endpoint they describe — and *are* removed, since a published request contract for a
route that no longer exists is a contract error rather than dead UI.

### Frontend

`changedFields` (`src/lib/corrections.ts`) narrows a form to what actually moved, comparing against
the normalised form the aggregate stores so retyping `  mrn-001  ` counts as no change — while
sending what the user actually typed, because trimming on the way out would quietly alter the
request. Sending every field back would claim to correct things nobody looked at, and would turn two
people editing two different fields into a fight over the whole record.

It is a separate module from `patients.ts` for the reason `redaction.ts` is separate from
`telemetry.ts`: `npm test` runs these through `node --test`, which resolves no bundler paths, so
testable logic lives where it can be imported alone. `tsconfig.test.json` lists it, as it does the
other two.

`isStale` moved from `admissions.ts` to `api.ts` — 412 is an HTTP fact, not an admissions one, and
patients now need it too.

### Tests added

Domain (18): `Patient.Correct` changes only what it names, normalises and trims as registration does,
refuses blanking a required field, refuses a future date of birth, refuses correcting nothing, leaves
the patient untouched when it refuses, and does not move its own version. `PatientService` writes and
publishes, refuses a replay at the version it quoted, 409s an MRN another patient holds, frees a
corrected MRN for reuse, and leaves exactly one winner among sixteen racing corrections.
Cross-domain: a correction reaches Admissions' read model.

API (14): `PatientCorrectionTests` — Location resolves at the same version; no `If-Match`, a weak tag
and `*` are each 428; a correction changes only what it names and advances the version; a replay is
412; an MRN collision is 409; an empty body, an unknown field and a blank name are 400s; an unknown
patient is 404; a viewer is 403; and the trail reads `AUDIT patient.correct`. `QueryRedactionTests`
covers the function and — through a real request with an `ActivityListener` attached — that the span
for a patient search does not carry the name searched for.

SQL (7): `PatientRepositoryTests` — a registered patient lands at version 0; a correction writes the
named columns and bumps the version; **a correction taken against a stale version writes nothing at
all**, not its own field and not the winner's back; the index refuses an MRN another patient holds;
a corrected MRN frees the old one; a patient that is gone is `NotFound` rather than a version
mismatch; and a corrected MRN still satisfies `ck_patients_mrn`.

Frontend (7): `corrections.test.ts` — an untouched form sends nothing, only moved fields are sent,
a retyped-but-identical MRN or name is not a change, and a value the user actually changed is sent
whitespace and all.

### Verified against the running app

SQL-backed API on :5028 with migration 004 applied to the dev database. The full ladder: 201 with
`ETag: "0"` and a `Location` that answers 200 at the same tag · no `If-Match` → 428 · `*` → 428 ·
`W/"0"` → 428 · `{}` → 400 · `{"registeredAt":…}` → 400 · correct at `"0"` → 200 with `ETag: "1"` ·
**replay at `"0"` → 412** naming version 1 · MRN onto a taken one → 409, record unchanged · padded
lowercase MRN → 200, stored normalised · unknown patient → 404 · viewer → 403 · admit an unknown
patient → **422** with `errors.patientId`.

In the browser (Patient flow → search → patient record → Correct details): a correction lands and the
record, the avatar initials and the search row all update; **a second correction from the same open
panel succeeds**, which is what proves the new version was carried back rather than re-fetched by
luck; a correction made out-of-band in between produces the 412 in the alert *and* refreshes the
panel and the form to what the record became, so the retry is decided against that and then succeeds;
an MRN collision shows `Conflict: A patient with MRN 'ALC-0001' already exists.` and leaves the record
alone. The only console errors in the session are those two deliberate failures. Hospital overview
still renders — 720 beds, 481 occupied, 239 available.

The API log shows `AUDIT patient.correct by nurse -> 200/412/409`, four `Corrected patient … to
version N` lines, and four `Admissions took the correction …` — the read model kept current through
the event seam, as rule 3 requires.

### Noted, not changed

- **`Patients.tsx` and `Admissions.tsx` are dead.** Deleting them is the right call and is not
  item 5's to make.
- **Patient search is not audited.** A viewer can search the whole register and leave no trail. ADR
  0004 records it: a more serious version of the same question, and not that decision's to answer.
- **A no-op correction still bumps the version.** `changedFields` stops the common case at the
  client, but `PATCH {"givenName":"Ada"}` on a patient already called Ada is a write and is counted
  as one. Honest, and cheap under `If-Match`.
- **The registration schemas floor dates of birth at 1875**, so the app cannot register the real Ada
  Lovelace (b. 1815). Pre-existing, and the correction schema keeps the same floor deliberately.

## Item 6 — OpenAPI contract batch — DONE

**Baseline: 231 passed** (46 domain, 144 API, 41 SQL). **After: 238 passed / 0 failed / 0 skipped**
(46 domain, 151 API, 41 SQL). Final `dotnet build`: 0 errors, 0 warnings on the incremental build.
Recompiling the API tests still reports the existing CTJ001 warnings; item 7 owns those.

### Tests first

`OpenApiMetadataTests` fetches the actual Development document through the host and asserts bearer
requirements, anonymous exceptions, JSON response media types, unique operation IDs, endpoint
descriptions, DTO summaries and positional-record property descriptions. All four failed before
the change. `AuthTests` signs otherwise-valid tokens with a 64-byte test key: HS256 already passed,
while HS384 and HS512 both incorrectly returned 200 before `ValidAlgorithms` was pinned. All seven
new cases now pass.

### What changed

- `OpenApi/BearerSecuritySchemeTransformer.cs` declares the HTTP bearer scheme. The operation
  transformer reads `IAuthorizeData` and honours `IAllowAnonymous`, so the eleven protected
  operations publish bearer requirements and 401/403 problem responses. Login, telemetry and
  health remain public; login retains its separate invalid-credentials 401.
- All fourteen operations, including `/health`, have stable names, summaries and descriptions.
  The patient GET already had its typed 200 from item 5. Hospital occupancy now also declares
  its existing 503 without losing its typed 200.
- API, API.Contracts and Hospital emit XML documentation. .NET 10's built-in OpenAPI generator
  includes the referenced projects' comments, including `WardDto.FreeBeds`, patient/admission
  versions and `HospitalSnapshot.AsOf`. DTOs receive summaries where none existed. CS1591 is
  suppressed, and CS1573 is suppressed where partial parameter documentation is intentional.
- JWT validation accepts `SecurityAlgorithms.HmacSha256` only.
- [ADR 0005](../../docs/adr/0005-https-is-enforced-at-the-host.md) records IIS as the HTTPS/HSTS
  enforcement point, with HTTPS binding, refusal of public HTTP requests and deployment checks.
  `Program.cs` and the backend README point to that decision. No remote IIS configuration was
  changed or claimed verified.

### The suggested Produces attribute changes runtime behaviour

The run sheet suggested `[Produces("application/json")]` on `ApiController`. Applied literally,
that attribute overwrote `application/problem+json` on existing domain/validation failures. Three
existing tests failed on the content type: the two unknown-patient 422 cases and the future-DOB
400 case. They were retained unchanged.

`JsonResponseOperationTransformer` instead removes the default formatter media types from the
document. The API continues returning its original problem content types, and the whole suite
passes. This is a contract-only change, as the item intended.

### Live document comparison and browser verification

Fetched `/openapi/v1.json` from a real Kestrel instance on :5036 before and after the change:

| Document fact | Before | After |
| --- | --- | --- |
| OpenAPI version / operation count | 3.0.4 / 14 | 3.0.4 / 14 |
| Operation IDs / summaries / descriptions | 0 / 0 / 0 | 14 / 14 / 14 |
| Operations declaring bearer authentication | 0 | 11 |
| Component schemas with descriptions | 5 | 13 |
| Response media types | text/plain, application/json, text/json | application/json, application/problem+json |

All six request-body definitions are unchanged in the comparison; the existing schema-contract
tests also pass. Browser verification used Playwright with a separate frontend on :5176 targeting
the changed API: nurse login, hospital overview (720 beds, 481 occupied, 239 available), patient
flow and an MRN search all succeeded. No browser console errors or warnings were observed.

## Next

Item 7 — housekeeping: correlation IDs on 500s, CTJ001 warnings and a manual `.http` request file.

## Environment note

The repo targets `net10.0` since checkpoint 10, and the SDK on `PATH` (`C:\Program Files\dotnet`)
tops out at 9.0.302. `global.json` at the repo root now pins 10.0.401 (`rollForward: latestMinor`),
so a build with the wrong SDK says which SDK it needs instead of failing NETSDK1045 on all ten
projects. The .NET 10 SDK is at `C:\Users\sam\.dotnet10`; until it is installed under
`C:\Program Files\dotnet`, build with `DOTNET_ROOT=C:/Users/sam/.dotnet10` and that directory ahead
on `PATH`.
