udit: backend against dotnet-webapi

Baseline: dotnet build — 0 errors, 146 warnings (all CTJ001, all in Alcidion.Api.Tests). dotnet test — 142 passed, 0 failed. OpenAPI document fetched from
a live instance on :5025 and inspected, so the findings below are what the API actually publishes, not what the attributes suggest.

The request side of this codebase is genuinely strong — schema-first bodies, per-field 400s, correct Problem Details mapping, CancellationToken threaded
everywhere, no entities on the wire. Everything below is the response side and the edges.

Real defects

1. GET /api/patients/{id} publishes no 200. PatientsController.cs:30 declares only [ProducesResponseType(404)]. Declaring any explicit response replaces
   MVC's inference, so the success case vanished — the live document lists 404 and nothing else for that operation. Any client generated from this document
   has no return type for fetching a patient. Contrast AdmissionsController.Discharge (:40), which declares nothing and correctly gets a 200 with
   AdmissionDto. Fix: add [ProducesResponseType<PatientDto>(200)].

2. The Location header on admit points at a route that doesn't exist. AdmissionsController.cs:36 returns Created($"/api/admissions/{a.Id}", ...), but
   there is no [HttpGet("{id:guid}")] on that controller — only the list. A client following the header gets a 404. Either add the GET-by-id action (my
   preference; PatientsController already has one and uses CreatedAtAction) or stop advertising a resource URL you don't serve.

3. No security scheme in the OpenAPI document. Every controller except auth/telemetry is [Authorize], and the document says nothing about it — no
   components.securitySchemes, no per-operation security, no 401/403 documented anywhere except login. Swagger UI can't authenticate and generated clients
   don't know to send a bearer token. Needs an IOpenApiDocumentTransformer adding the bearer scheme plus an operation transformer that reads the authorize
   metadata.

4. Every response advertises text/plain. The document lists ["text/plain","application/json","text/json"] for AdmissionDto, PatientDto, HospitalSnapshot
   and the rest. That's the default formatter set leaking into the contract; nothing serves those DTOs as text. One [Produces("application/json")] on
   ApiController fixes all of them.

5. XML doc comments produce nothing. No project sets GenerateDocumentationFile. The result is asymmetric documentation: request schemas carry rich
   per-property descriptions (they come from the JSON Schema documents), while WardDto, PatientDto, AdmissionDto, MeResponse and HospitalSnapshot publish
   bare types with no title and no description. The <summary> on WardsController and the <param name="FreeBeds"> note on WardDto are dead text. Set the
   property, silence CS1591 if you don't want it enforced everywhere.

6. No operation metadata at all. Zero summaries, descriptions, or operation IDs across all 13 operations. [EndpointSummary]/[EndpointDescription] are the
   controller-side equivalents of the skill's .WithSummary().

7. Telemetry ingest is an unauthenticated, unbounded log sink. TelemetryController is [AllowAnonymous], has no rate limit and no size cap beyond Kestrel's
   30 MB default, and logs caller-supplied props verbatim at Information. The schema caps the batch at 500 items but puts no bound on props, which is open
   by design. In a system where the frontend is clinical, that's an anonymous path for arbitrary caller content — plausibly PHI — into your log pipeline,
   plus a cheap log-volume DoS. At minimum: [RequestSizeLimit], a fixed-window rate limiter keyed by session, and a maxLength/maxProperties bound in the
   schema.

8. 202 response body is undocumented and untyped. TelemetryController returns an anonymous { received, correlationId }; the document shows a 202 with no
   content. Make it a sealed record.

Security

9. A working JWT signing key is committed in appsettings.json. "dev-only-secret-change-me-in-production-0123456789" sits in the base file, not
   appsettings.Development.json, so a deployment that forgets to override it runs on a key that's in git. Move it to the Development file and make Program.cs
   throw at startup when Jwt:Secret is empty outside Development. Right now JwtOptions.Secret defaults to "" and SigningKey would happily build a
   zero-length HMAC key.

10. No ValidAlgorithms restriction on the token validation parameters (Program.cs:52). Cheap hardening: pin ["HS256"].

11. No UseHttpsRedirection() and no HSTS. Defensible behind a terminating proxy; worth a deliberate decision rather than an omission.

Skill deviations worth a decision, not a fix

- No service interfaces. PatientService and AdmissionService are concrete and injected directly (AddScoped<PatientService>()). The skill wants
  IPatientService. I'd leave it: your repositories and the event bus are already behind interfaces, the domain tests exercise the services against
  in-memory stores, and adding a one-implementation interface here buys nothing but a file.
- No .http file anywhere in the backend. This one I'd add — you have 13 endpoints, four demo logins and a JWT flow, and nothing in the repo shows how to
  drive them by hand.
- No Middleware/ folder. CorrelationIdMiddleware lives in Observability/, which reads better than the skill's rule. Leave it.
- JsonStringEnumConverter not configured. No enum currently reaches the wire — AdmissionDto stringifies Status by hand at :14. Latent: the first
  enum-typed DTO property serializes as an integer. Configure it now, or keep stringifying by hand deliberately.
- 146 CTJ001 warnings. All in Alcidion.Api.Tests, all "use a UTF-8 literal". Step 8 wants a clean build. Either fix them or <NoWarn>CTJ001</NoWarn> in the
  test csproj — a warning nobody intends to act on trains you to ignore the list.

Not a problem

app.MapGet("/health") mixes a minimal API into a controller project, which the skill forbids. It's one line for a liveness probe and reads fine. If you
want it consistent, note that you also have no AddHealthChecks() and the endpoint reports "ok" without checking the database — that's the more useful
thing to fix.

What the verb-level pass turns up

1. The cache directives are exactly inverted. [ResponseCache(NoStore = true)] appears once in the codebase — on HospitalOccupancyController.cs:9, the one
   controller whose own class comment says it emits "occupancy counts only - no patient identifiers or demographics." Meanwhile GET /api/patients/{id}
   returns MRN, given name, family name and date of birth with no cache directive at all, as do /api/patients, /api/wards, /api/admissions and /api/auth/me.

GET is cacheable by default — that's the basic. The protected endpoint got the protection; the PHI endpoints didn't. Fix is one line:
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] on ApiController, delete the one-off.

2. Non-idempotent POST on operations the client will retry. POST /api/admissions/{id}/discharge and /transfer. POST means "a retry is a second operation,"
   and for transfer that's not theoretical: TransferAsync:46 only requires the admission be open, so a replayed transfer closes the stay it just created,
   allocates a second bed, and writes a duplicate bed_request — a success response and a corrupt bed history. Discharge at least fails closed with a 409.

The UI disables the button while busy (PatientFlow.tsx:214), which covers the double-click and nothing else — not a network-layer retry, not a proxy
replay.

Worth noting these are the same two operations from the race in my last review, and the canonical fix covers both: model them as PATCH
/api/admissions/{id} carrying the state change, gated on If-Match against ConcurrencyVersion. That makes the retry safe and closes the lost-update window
in one change. If you'd rather keep the action sub-resources — defensible, Stripe and GitHub both do it — then an Idempotency-Key header is the
alternative, and it's more work.

3. PHI in the query string. GET /api/patients?search= and the frontend puts the typed patient name straight into it (PatientFlow.tsx:50). Query strings
   land in access logs, proxy logs, browser history and Referer. This is the standard GET-vs-POST-for-search tradeoff and it should be a recorded decision,
   not a default.

4. No PUT, PATCH or DELETE anywhere. Thirteen operations, all GET or POST. Patient records are write-once — register and read, never correct. An MRN typo
   or a legal name change has no path through this API.

5. POST /api/admissions advertises 404 (AdmissionsController.cs:29). The 404 refers to the patient named in the body, not the posted URL. A client
   reasonably reads 404-on-POST as "that endpoint doesn't exist." 422, or a 400 naming patientId, is the honest code.

Why both audits missed this

Each had an oracle, and verbs fall outside both.

claude's audit ran off a framework checklist and the emitted OpenAPI document. A generated document renders verbs as fact — it reports that post
/api/admissions/{id}/discharge exists; it has no way to say it shouldn't be a PATCH. That audit could only find things that are declared wrong, never
things that are designed wrong. Look at the shape of its findings: 1, 4, 5, 6 and 8 are all "an attribute is missing." That is precisely what a checklist
can see.

astra's audit was reproduction-led — it hunted behaviour it could break on a live instance. Verb misuse doesn't break. A non-idempotent POST returns 200.
A cacheable PHI GET returns 200. There is no failure to reproduce, so a reproduction-driven audit is blind to this class by construction.

Presence-checking beats placement-checking. [ResponseCache] exists in the codebase, so any "is caching handled?" check passes. Noticing it's on the wrong
controller requires knowing which endpoints carry patient identifiers — domain knowledge, not framework knowledge, and neither audit was operating with a
PHI map in hand.

Both read the comments as specification. This codebase documents itself unusually well, and that cuts both ways. TransferAsync:46 states "a concurrent
transfer or discharge blocks here rather than interleaving with the stay rewrite below" — that comment is wrong, and it sits directly above the bug astra
found by testing rather than reading. Everywhere else, stated intent was largely taken at face value. Confident prose is the most effective camouflage in
a codebase like this one.

Absences don't show up in enumerations. Both audits walked what exists. No PUT/PATCH/DELETE means nothing looks like verb misuse — the surface is GET and
POST, which reads as unremarkable. The missing update operations are the finding, and you only get there by asking "what should this API be able to do?",
which neither pass asked.

Neither was briefed for it. One was pointed at a webapi skill, the other at runtime correctness and security. Both delivered their brief. Resource-model
coherence was a third pass nobody ran.

The general lesson: an audit finds what its oracle can express. Checklist gives you missing attributes. Reproduction gives you runtime failures. Design
coherence has no oracle — it needs a read with judgment and no checklist, which is the pass that catches verbs, cache semantics and idempotency.

Revised order

1. Discharge/transfer race and transfer idempotency — one If-Match + version-checked PATCH fixes both.
2. NoStore onto ApiController.
3. Telemetry bounds and rate limit.
4. JWT secret plus the startup guard block.
5. GET /api/admissions/{id}, then the attribute batch.

Items 1 and 2 are the ones I'd not ship without.
