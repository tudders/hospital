Found six actionable issues. The most serious is a discharge/transfer race that leaves discharged patients occupying beds.

1. High — Discharge can leave an open bed stay. If discharge captures its timestamp before a concurrent transfer commits, its update skips the newer stay
   but still commits successfully. Reproduced against SQL Server: Discharged, with one open stay remaining. Validate concurrency and close the current
   stay atomically. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Admissions/Infrastructure/EfAdmissionRepository.cs:213

2. High for production — Demo admin credentials remain active. In an isolated instance running with Production, admin/admin returned a valid administrator
   token. Changing the signing secret does not disable these accounts. Restrict demo authentication to an explicit demo mode and configure production
   identity separately. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Api/Auth/DevTokenIssuer.cs:16

3. High for production — Missing database configuration silently enables volatile storage. Production starts successfully and accepts patient/admission
   writes into memory when the connection string is absent. Those records disappear on restart. Require database configuration outside an explicit demo/
   test mode. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Patients/PatientsModule.cs:26

4. Medium — Validated telemetry can still throw a 500. The schema permits integers larger than the long conversion supports. Sending seq or t as
   9223372036854775808 reproduced a 500; normal values returned 202. Add schema upper bounds matching the destination type. /C:/Users/sam/Desktop/DEV/
   alcidion/backend/src/Alcidion.Api.Contracts/ClientEventBatch.cs:69

5. Medium — Admission creation returns a broken resource URL. POST /api/admissions returns 201 with /api/admissions/{id}, but there is no corresponding
   GET action. Following the returned Location produced 404. Implement the resource endpoint. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Api/
   Controllers/AdmissionsController.cs:36

6. Medium — Exception responses lose correlation IDs. Both reproduced 500 responses omitted X-Correlation-Id. Exception handling clears the header after
   the correlation middleware unwinds, also ending its logging scope. Preserve correlation through error handling and response generation. /C:/Users/sam/
   Desktop/DEV/alcidion/backend/src/Alcidion.Api/Program.cs:88

Verification: 142 tests passed, including 32 SQL tests. NuGet reported no known vulnerable dependencies from its configured sources. The Web API skill
guided the contract checks. No repository files were changed; the isolated server was stopped and scratch database removed.

• The API partially follows the dotnet-webapi skill. The main gaps are in its published contract and documentation.

On your example: we currently use POST for discharge and transfer, with no PATCH endpoints.

Operation Current implementation Assessment
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Register patient / admit patient POST → 201 Appropriate creation semantics; admission’s Location is broken
────────────────────────────────── ─────────────────────────────────────────── ────────────────────────────────────────────────────────────────
Discharge admission POST /api/admissions/{id}/discharge → 200 Reasonable workflow command
────────────────────────────────── ─────────────────────────────────────────── ────────────────────────────────────────────────────────────────
Transfer admission POST /api/admissions/{id}/transfer → 200 Reasonable workflow command
────────────────────────────────── ─────────────────────────────────────────── ────────────────────────────────────────────────────────────────
Selectively edit patient fields Not implemented Use PATCH /api/patients/{id} when added

The skill groups partial updates/actions under PATCH. However, POST is valid for commands, while PATCH applies a described modification to a resource. My
recommendation is to retain POST for these workflows and use PATCH for selective field editing. Transfer includes allocating a bed and closing/opening
stays; changing the verb alone would not improve that implementation. HTTP POST semantics, PATCH specification

The implementation findings are:

1. OpenAPI omits a successful patient lookup response.
   GET /api/patients/{id} advertises only 404, despite returning a patient with 200. Generated clients cannot reliably derive its successful response
   contract. Add explicit typed 200 metadata. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Api/Controllers/PatientsController.cs:30

2. Authentication and error responses are underdocumented.
   The generated document has no bearer security scheme or operation security requirements. Transfer/discharge advertise only 200; occupancy omits its
   400/503 outcomes. Runtime authorization exists, but clients cannot discover these requirements from the contract. /C:/Users/sam/Desktop/DEV/alcidion/
   backend/src/Alcidion.Api/Program.cs:34

3. Admission creation points to an unimplemented resource endpoint.
   The 201 response’s Location targets /api/admissions/{id}, but there is no GET action there. Add it and use CreatedAtAction. /C:/Users/sam/Desktop/DEV/
   alcidion/backend/src/Alcidion.Api/Controllers/AdmissionsController.cs:36

4. Endpoint descriptions and response contracts need completion.
   All 13 published operations lack summaries. Telemetry returns an anonymous receipt, but its documented 202 response has no body schema. Use a named
   response record and explicit response metadata. /C:/Users/sam/Desktop/DEV/alcidion/backend/src/Alcidion.Api/Controllers/TelemetryController.cs:28

5. Some skill conventions are missing.
   Patient and admission services are injected as concrete classes, whereas the skill requires service interfaces. There are no .http request examples.
   These are maintainability/documentation gaps rather than demonstrated runtime failures.

Several parts already align: thin clinical controllers, separate response DTOs, DateTimeOffset timestamps, cancellation propagation through asynchronous
endpoints, and centralized Problem Details.

The generated request structs are an intentional exception to the skill’s sealed-record guidance: ADR 0001 (/C:/Users/sam/Desktop/DEV/alcidion/docs/
adr/0001-schema-first-request-validation.md) explicitly establishes schema-generated requests. I would preserve that architecture.

## Completed API implementation assessment — 2026-09-14

Assessment of the API at `3c164fc`, using `backend/skills/vendor/dotnet-webapi/SKILL.md`, the backend's first-party rules, and ADR 0001. The earlier findings above remain open; this section consolidates their significance and completes the skill checklist. This is an assessment, not an implementation of fixes.

The API has a consistent controller/service structure and substantial existing tests, but its published HTTP contract is incomplete. The discharge/transfer race is the highest-priority correctness defect. POST versus PATCH is a design decision to document, not the main defect to fix.

### HTTP method decisions

| Operation | Current method and success response | Assessment |
|---|---|---|
| List/read patients, admissions, wards, occupancy and current user | GET / 200 | Appropriate. Admission lookup by ID is missing. |
| Register a patient | POST / 201 with Location | Appropriate; uses CreatedAtAction. |
| Admit a patient | POST / 201 with Location | Appropriate method/status, but Location targets an unimplemented GET endpoint. |
| Discharge an admission | POST / 200 with updated admission | Valid command design; document as a workflow transition. |
| Transfer an admission | POST / 200 with updated admission | Valid command design; the operation closes and opens bed stays and allocates capacity. |
| Login | POST / 200 with token | Appropriate for this authentication exchange; a token response does not require 201. |
| Ingest telemetry | POST / 202 with receipt | Clarify whether this acknowledges acceptance for downstream processing or completion. If completion is the promise, 200 with the receipt is clearer. |
| Selectively edit patient attributes | Not implemented | Prefer PATCH on the patient resource when introduced, with a defined patch format and allowed fields. |
| Replace/delete a resource | No PUT or DELETE endpoints | Not applicable; do not add methods solely to satisfy a generic checklist. |

The vendor skill's table groups partial updates/actions under PATCH. HTTP also permits resource-specific processing through POST. Retaining these existing command endpoints is my recommendation, not literal compliance with that simplified table. PATCH can also have side effects on related resources, so multiple database writes alone do not rule it out. A PATCH design would need an explicit modification contract and atomic application; changing the attribute from HttpPost to HttpPatch is insufficient. Neither method automatically makes retries safe.

Sources: [HTTP POST semantics](https://httpwg.org/specs/rfc9110.html#POST), [PATCH semantics and atomicity](https://www.rfc-editor.org/rfc/rfc5789).

### Findings and acceptance criteria

| Priority | Finding and source | Required outcome |
|---|---|---|
| P1 | Discharge can commit after a later transfer while leaving its bed stay open. `backend/src/Alcidion.Admissions/Infrastructure/EfAdmissionRepository.cs:213` | A stale discharge must either fail with a conflict and no partial writes, or complete against the current state with a valid timestamp and no remaining open stay. Add a SQL regression for the reproduced interleaving. |
| P1 for non-demo deployment | Demo admin login is enabled in Production. `backend/src/Alcidion.Api/Auth/DevTokenIssuer.cs:16` | Production cannot issue privileged tokens using demo credentials. Demo access must be an explicit deployment mode. |
| P1 for non-demo deployment | Missing connection configuration enables volatile patient/admission storage. `backend/src/Alcidion.Patients/PatientsModule.cs:26` and `backend/src/Alcidion.Admissions/AdmissionsModule.cs` | Non-demo deployments fail startup or reject clinical writes when required persistence is unconfigured. |
| P2 | Admission creation returns a Location that responds with 404. `backend/src/Alcidion.Api/Controllers/AdmissionsController.cs:36` | Following Location after 201 returns 200 with the created admission; an unknown ID returns 404. |
| P2 | Patient lookup publishes only 404, omitting its successful response. `backend/src/Alcidion.Api/Controllers/PatientsController.cs:30` | The generated document declares typed 200 and Problem Details 404 responses. |
| P2 | OpenAPI declares no bearer security scheme or operation security requirements. `backend/src/Alcidion.Api/Program.cs:34` | Protected operations declare bearer authentication; intentionally anonymous operations remain anonymous in the contract. This is a documentation defect, not evidence that runtime authorization is absent. |
| P2 | Error metadata is incomplete: discharge/transfer publish only 200; occupancy omits 400/503; protected operations omit applicable 401/403. Relevant controllers under `backend/src/Alcidion.Api/Controllers/` | Published statuses and error bodies match actual outcomes. Include both edge-validation and domain-validation error shapes where applicable. |
| P2 | Telemetry's unbounded schema integers overflow conversion to long. `backend/src/Alcidion.Api.Contracts/ClientEventBatch.cs:69` | Out-of-range seq/t values produce a field-level 400, with bounds also visible in the published schema. Test the maximum accepted value and the next value. |
| P2 | Exception responses lose X-Correlation-Id and exception logging occurs after its custom scope unwinds. `backend/src/Alcidion.Api/Program.cs:88`, `backend/src/Alcidion.Api/Observability/CorrelationIdMiddleware.cs:22` | An induced 500 retains the supplied/minted correlation ID, and the error log carries it. Preserve Problem Details handling. |
| P2 verification gap | Only patient registration has a forbidden-viewer assertion; admission, discharge and transfer do not. Transfer has SQL tests but no HTTP integration test. `backend/tests/Alcidion.Api.Tests/AuthTests.cs`, `PatientFlowTests.cs` | Cover anonymous, viewer, clinician and admin behavior on every clinical write, including valid and invalid transfer requests. A denied request must leave state unchanged. |
| P3 | All 13 published operations lack summaries; response DTO descriptions are incomplete. Controller response records and operation metadata | Publish useful summaries/descriptions and verify the actual generated document. Adding source comments alone is not proof they appear. |
| P3 | Telemetry uses an anonymous receipt and its 202 response has no published body schema. `backend/src/Alcidion.Api/Controllers/TelemetryController.cs:28` | Use a named immutable response contract and declare its body schema. |
| P3 | PatientService, AdmissionService and DevTokenIssuer are injected as concrete implementations. Their controllers and registrations | Either follow the skill's service-interface convention or document this as an intentional local exception. Existing services already depend on repository abstractions; this is not a demonstrated correctness defect. |
| P3 | No .http request examples exist. | Supply runnable examples covering every endpoint and representative failures, using the configured development port. |

There are two distinct 400 response shapes today: schema failures return ValidationProblemDetails with `errors`, while domain validation maps to plain ProblemDetails through ApiController.FromError. Both are legitimate responses, but documenting only the field-error shape is incomplete. A future-date patient registration is an example of domain validation. Retain the first-party Result-to-HTTP mapping and accurately describe the contract.

### Full skill checklist disposition

| Skill area | Disposition |
|---|---|
| Consistent controller/minimal-API style | Clinical endpoints consistently use controllers. `/health` uses a minimal handler: a literal convention exception, with no demonstrated benefit from moving it solely for uniformity. |
| Thin controllers and service layer | Clinical actions translate input, invoke services and map results. No EF entities are bound or returned directly. Read-model interfaces on wards/occupancy are appropriate boundaries. Service interfaces are incomplete as noted above. |
| Dedicated immutable DTOs | Named response types are sealed records. Generated request structs are explicitly required by ADR 0001 and take precedence over the vendor's sealed-record request convention. Telemetry/health use anonymous response objects. |
| DTO naming | PatientDto, AdmissionDto and WardDto differ from the skill's ResourceResponse naming convention. Low-impact consistency consideration; not a wire-level defect. |
| Date/time representation | Instants use DateTimeOffset; date of birth uses DateOnly, appropriate for a calendar date. |
| Enum representation | Admission status is explicitly emitted as a string. Current status DTO fields are strings, so there is no demonstrated numeric-enum serialization defect. Their OpenAPI schemas do not enumerate allowed status values. |
| Validation | Every controller request body is schema-generated, with structural tests enforcing this. Missing/null login fields produced 400 for both application/json and a vendor +json media type. Telemetry integer bounds remain a concrete gap. |
| JSON compatibility | Preserve the accepted schema-based wire contract. Do not impose new serializer restrictions just to copy a generic skill example. |
| Cancellation | Asynchronous controller/service/repository paths inspected pass CancellationToken. Synchronous login, me, telemetry and health omit it: a literal skill deviation, but adding an unused token is not a useful fix. |
| HTTP success/error semantics | GET/create conventions mostly align. Missing admission lookup and incomplete response metadata need repair. POST command endpoints are defensible. PUT/DELETE checks are not applicable. |
| OpenAPI infrastructure | Built-in AddOpenApi/MapOpenApi is present and the document loads in Development. Schema constraints are published through the custom transformer. Operation/security/response metadata needs completion. |
| Problem Details and exceptions | Global exception/status middleware and centralized domain error mapping are present. No custom exception-handler class is required merely to satisfy the skill's placement advice. Numeric conversion and correlation failures remain open. |
| Authorization/auditing | Clinical writes carry Clinician policy and Audited attributes. Required forbidden-role coverage is incomplete. Demo authentication requires an explicit deployment boundary. |
| Request examples | Missing .http files. Existing integration tests do not replace this documentation requirement. |
| Build/test verification | The session's dotnet test run built the solution and passed 142 tests: 28 domain, 82 API and 32 real SQL tests, with no reported skips or build warnings. OpenAPI and targeted HTTP/SQL reproductions were additionally exercised. No full-suite rerun was needed for this documentation-only completion. |

### Evidence and limits

- Inspected all seven controllers, startup/authentication wiring, request schemas/formatter, response contracts, domain error mapping, relevant persistence paths and existing API/SQL tests.
- Observed the live OpenAPI document: 13 operations, no operation summaries, no security declarations, missing patient-lookup success metadata, and missing telemetry success-body metadata.
- Reproduced the discharge/transfer interleaving against a scratch SQL database: transfer succeeded, discharge reported success, persisted status was Discharged, and one bed stay remained open. The fixture removed the scratch database afterward.
- Reproduced admission Location returning 404, oversized telemetry seq/t producing 500, and missing correlation headers on those 500 responses.
- Confirmed demo admin authentication and volatile writes in an isolated local instance configured as Production. These are deployment risks for a non-demo system; no live deployment was probed.
- NuGet's configured sources reported no known vulnerable direct or transitive packages at the time of the session check. This does not establish runtime support status or replace a deployment security assessment.
- No application fixes have been made by this assessment. Temporary HTTP processes were stopped. The only change in this completion is this report.

Recommended implementation order: fix the state-consistency defect; establish explicit demo/production boundaries; repair validation and correlation failures; complete resource lookup and OpenAPI contracts; add missing HTTP/authorization regressions; then finish documentation and lower-impact conventions. Keep POST for the existing workflow commands unless a deliberate API contract redesign is requested.
