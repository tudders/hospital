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
