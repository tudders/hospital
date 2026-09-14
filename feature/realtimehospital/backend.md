# Real-time hospital: backend handoff for Luna

Status: implementation work breakdown; design decisions remain subject to [plan.md](plan.md).  
Owner: backend implementer. Frontend counterpart: [frontend.md](frontend.md).

## Outcome and scope

Deliver SQL-authoritative patient locations and movement history, atomic/idempotent staff and generator commands, durable outbox delivery through a separate Redis server, authenticated live updates with recovery, and a deterministic movement generator.

Work in `backend/`, plus shared contract/operational documentation in this feature folder. Read `backend/.agents/AGENTS.md` and relevant local playbooks before implementation. Use the codebase knowledge graph for discovery. Implement each chunk with behavior-first tests, preserving context boundaries, thin controllers, `Result<T>`, `IClock`, correlation scopes and audit conventions.

Current entry points:

- `database/001_initial_hospital_schema.sql` and `002_seed_mock_data.sql`: schema and seed baseline.
- `src/Alcidion.Admissions/Domain/Admission.cs` and `Application/AdmissionService.cs`: admission lifecycle.
- `src/Alcidion.Admissions/Infrastructure/InMemoryAdmissionRepository.cs` and `InMemoryKnownPatients.cs`: current in-memory persistence/read model.
- `src/Alcidion.Patients/Infrastructure/InMemoryPatientRepository.cs`: current patient storage.
- `src/Alcidion.Api/Controllers/AdmissionsController.cs`: HTTP conventions.
- `tests/Alcidion.Domain.Tests` and `tests/Alcidion.Api.Tests`: existing test projects.

The existing SQL schema is not proof that the application uses SQL. Explicitly bridge the current in-memory patient/admission workflow to the same durable records used by movement; the feature cannot have two conflicting admission authorities.

## Delivery order and ownership

| Chunk | Depends on | Frontend unblocked |
| --- | --- | --- |
| BE-01 contract and decisions | Source plan and repository inspection | FE-01 and fixture-driven UI |
| BE-02 migration and persistence | BE-01 | Durable test data |
| BE-03 snapshot/history reads | BE-02 | FE-02 integration |
| BE-04 movement transaction/API | BE-02, BE-01 | FE-03 integration |
| BE-05 Redis and outbox relay | BE-04 | Committed event distribution |
| BE-06 live gateway and replay | BE-03, BE-05 | FE-04 integration |
| BE-07 deterministic generator | BE-04 | Generator/staff acceptance traffic |
| BE-08 recovery and operations | BE-03 through BE-07 | FE-05 end-to-end acceptance |

BE-03 and BE-04 can be developed independently once persistence and contracts are ready. BE-07 generation logic can proceed while live delivery is being implemented. This ordering describes work dependencies, not a requirement to launch multiple agents.

## BE-01 — Publish the shared contract and resolve implementation choices

- [ ] Create `feature/realtimehospital/api-contract.md` with request/response/event/error fixtures and keep implemented OpenAPI aligned with it.
- [ ] Define layout, locations, history, movement and live endpoints from plan section 11, including hospital scope and history pagination where needed.
- [ ] Define authoritative full location paths, occupancy states/counts, unlocated admissions, permissions/redacted display identity, command success and all conflict codes.
- [ ] Specify canonical request hashing, idempotency scope/retention and response replay, correlation handling, actor/source derivation and `clientMutationId` matching to response/event.
- [ ] Distinguish admission concurrency version, admission event sequence, opaque stream cursor and generator run sequence. Define version serialization against the actual SQL column type; do not assume a SQL rowversion is the plan's sample integer.
- [ ] Define a race-free snapshot/replay boundary, cursor expiration/gap response, sequence behavior and event schema handling. A maximum allocated SQL identity alone is not a safe commit watermark when transactions commit out of order.
- [ ] Specify initial bed allocation, same-bed requests, discharge closing an active stay, occurrence-time validation and historical/backdated moves. These must be consistent with active-admission and half-open interval rules.
- [ ] Record decisions from plan section 15. Recommended starting choices: dedicated append-only `patient_movements`; SSE using bearer-authenticated streaming; deterministic seed backfill through one demo hospital/floor and stable room assignments.
- [ ] Record role/hospital/ward authorization, identity redaction, retention, latency/recovery objectives and operator replay permissions. Mark unresolved deployment/policy values explicitly rather than presenting recommendations as approved policy.

Acceptance: the frontend can implement typed fixtures without guessing transport fields, version/cursor semantics, error codes or permissions. Decisions and remaining questions are visible in the shared document.

## BE-02 — Hierarchy migration and durable persistence

- [ ] Add an ordered migration after the existing scripts for hospitals, floors, rooms, ward/floor and bed/room ownership, active flags and parent-scoped code uniqueness.
- [ ] Backfill existing wards/beds deterministically; document exact mappings, preserve existing IDs/references and reconcile any retained legacy ward relationship with the strict hierarchy.
- [ ] Add typed location models and SQL repository boundaries. Preserve Admissions-to-Patients contract/event boundaries when making the patient/read-model/admission path durable.
- [ ] Ensure patients registered and admitted through existing APIs are available to the movement path and survive restart. Coordinate discharge with location state so it cannot leave an occupied bed for an inactive admission.
- [ ] Enforce one open stay per admission and per bed, historical non-overlap and valid entity relationships, including exact `[started_at, ended_at)` boundaries.
- [ ] Add movement history, durable command idempotency, unique-event outbox, relay lease/retry fields and supporting indexes. Reuse `flow_events` for the audit reference under the BE-01 history decision.
- [ ] Add development database configuration and deterministic seed/test setup without committing secrets. Document migration application and recovery precautions for existing databases.

Acceptance: migration works against a fresh database and an existing seeded database; the full hierarchy is queryable; registration/admission/location state is coherent across application restart; SQL constraints reject duplicate active occupancy and invalid relationships.

## BE-03 — Authoritative snapshot and history endpoints

- [ ] Implement `GET /api/hospital-layout`, `GET /api/patient-locations?hospitalId=...` and `GET /api/patient-locations/{admissionId}/history`.
- [ ] Return full hierarchy paths, relevant bed status/capacity, active admission location/version and role-appropriate identity. Derive location from SQL open stays.
- [ ] Implement the BE-01 snapshot consistency and replay-boundary protocol. Account for layout/location requests observing different times; document the synchronization mechanism.
- [ ] Enforce authenticated role and hospital/ward scope on reads/history and return existing problem-details responses for forbidden or missing resources.
- [ ] Ensure reads remain correct when Redis is down. Optional caches cannot become the occupancy authority.

Acceptance: snapshots/history match SQL after transfers and discharge; scope/redaction tests pass; an event committed during bootstrap is replayed or represented by the snapshot without a gap.

## BE-04 — Atomic movement command and API

- [ ] Implement one application command handler shared by HTTP staff moves, generator commands and future integrations. Add `POST /api/patient-movements` as a thin authenticated/audited controller.
- [ ] Validate request shape and authorization; derive actor/source from trusted context and verify patient/admission/source-bed references against persisted state.
- [ ] In one SQL transaction with `XACT_ABORT ON`, lock/insert scoped idempotency, then lock admission, source/target beds in stable ID order and required hierarchy/capacity rows.
- [ ] Validate active admission/ancestors, source, target status and availability, expected version, capacity and timestamp rules under those locks.
- [ ] Close the old stay and open the new stay at the same valid UTC instant, append immutable movement/audit records, advance the aggregate version once, and insert the complete versioned event in the outbox.
- [ ] Persist success status/body in the idempotency record and commit before returning. Never call Redis within the transaction.
- [ ] Return the original result for an identical retry, including concurrent retries; return `409 IdempotencyKeyReuse` for a changed canonical payload. Implement stale-version and unavailable-bed conflicts with current state, `404` and validation problems per BE-01.
- [ ] Use bounded retry/backoff for transient SQL deadlocks/timeouts only; preserve the original command identity through retry and propagate correlation via existing scope conventions.
- [ ] Audit rejection metadata where policy permits without retaining sensitive payloads or introducing partial clinical writes.

Acceptance: successful moves create exactly one logical movement, audit event and outbox event with one active stay. Competing moves to one bed yield one winner; competing moves of one admission cannot leave two locations. Faults roll back all transaction effects. Redis outage does not prevent SQL success; response loss followed by retry returns the stored result.

## BE-05 — Separate Redis server and recoverable outbox relay

- [ ] Add a reproducible local Redis service/setup and environment-based connection configuration. Document private-network deployment, authentication/ACLs, TLS, persistence and resource limits.
- [ ] Implement batch SQL claims with expiring leases, bounded backoff/jitter, attempt/error metadata and durable publication status.
- [ ] Publish committed envelopes to `hospital:{hospitalId}:patient-movements:v1`. Mark published only after acknowledgement; use stable logical event IDs across duplicate publications.
- [ ] Define event ordering/replay behavior under multiple relay workers and retries. Per-admission sequence and cursor recovery must tolerate delayed publication without silently skipping a committed movement.
- [ ] Configure the required Stream consumers/groups, pending-entry reclamation, retention, dead-letter handling and replay procedures. Pub/Sub is only an optional wake-up signal.
- [ ] Acknowledge consumption after durable application or safe gateway handoff as appropriate; preserve browser replay independently of server-side acknowledgements.
- [ ] If implementing optional projections, deduplicate by event ID, enforce monotonic admission sequence, set TTLs and rebuild from SQL. Avoid demographics in Redis payloads.

Acceptance: Redis downtime accumulates recoverable outbox work; relay crash before/after acknowledgement loses no logical movement; duplicate publications are harmless; expired claims/pending entries recover; retained event data follows the documented policy.

## BE-06 — Authenticated live gateway, replay and gap recovery

- [ ] Implement the BE-01 transport choice at `/api/realtime/patient-movements`; default proposal is SSE because commands remain HTTP.
- [ ] Use existing bearer authentication and hospital/ward scope checks. Support the frontend's authenticated streaming client, required CORS headers and reconnect cursor metadata without tokens in URLs.
- [ ] Serve replay then live delivery with a synchronized handover; return an explicit gap/snapshot-required signal when a cursor cannot be served.
- [ ] Deliver every relevant event to every authorized connected client, including clients on different API instances. A single competing-consumer group across gateway instances is not sufficient broadcast fan-out by itself.
- [ ] Handle disconnect cancellation, heartbeats, bounded slow-client buffers and backpressure; slow clients may reconnect through replay rather than consuming unbounded memory.
- [ ] Enforce the same redaction/scope policy during live delivery and replay as during snapshots. Handle token expiry and prevent unauthorized cross-hospital events.
- [ ] Verify streaming/proxy buffering, idle timeouts and worker lifecycle for IIS/SmarterASP.NET. Document a separate worker host if the deployment cannot reliably run continuous relay/generator processes.

Acceptance: multiple clients/instances receive the same committed moves; bootstrap/reconnect races cause no silent gaps; expired cursors recover through snapshots; unauthorized scopes fail; clients converge after Redis recovery.

## BE-07 — Deterministic movement generator and admin API

- [ ] Reuse existing simulation scenarios/runs/events for immutable scenario versions, seeded runs, engine version, starting snapshot, durable progress, command IDs and recorded outcomes.
- [ ] Generate the same command identities/order from the same seed, scenario/engine version and starting snapshot. Keep deterministic generation separate from live contention outcomes, which may differ.
- [ ] Submit through BE-04 with trusted `source=generator`; derive stable idempotency keys from run/command identity and retain `runId` plus a distinct run sequence in emitted events.
- [ ] Implement start, status, pause, resume and cancel endpoints from plan section 11 with admin-only authorization, correlation, idempotency and problem details on writes.
- [ ] Add rate limits, worker ownership/expiring leases, bounded transient retries and durable conflict/failure outcomes. Define explicit retry/skip/cancel behavior; do not silently advance past failed commands.
- [ ] Add dry-run validation without clinical-state writes or movement/outbox publication; record run/report metadata as appropriate. State that validation is point-in-time and does not reserve beds.
- [ ] Resume after process failure using durable command identity/progress. If a command committed before run progress was saved, retry that same command to recover its recorded result.

Acceptance: deterministic replay test passes; pause/resume/cancel and dry run behave as documented; crash/restart never creates new identities for an existing command; staff and generator contention uses identical SQL rules. General staff cannot operate the generator.

## BE-08 — Failure verification, observability and operations

- [ ] Add focused domain/API tests for plan section 14; use real SQL Server integration tests for migrations, locking, constraints and rollback rather than claiming in-memory tests prove those properties.
- [ ] Exercise API failure before commit and after commit/before response, deadlock/timeout, same-key concurrency, exact interval boundaries, stale source/version, blocked/inactive/wrong-hierarchy targets and capacity rules.
- [ ] Exercise Redis outage/data loss, relay crash after acknowledgement, consumer crash/reclamation, duplicate/out-of-order delivery, multi-instance fan-out and expired browser cursors.
- [ ] Exercise generator crash/resume and concurrent staff/generator commands. Verify SQL alone reconstructs current location and history.
- [ ] Add metrics/structured logs for oldest outbox age, pending/failed counts, publication lag, consumer pending age, dead letters, reconnect/gap counts and generator progress/failures. Keep patient data out of telemetry.
- [ ] Document alert thresholds, retention, SQL-to-Redis rebuild, dead-letter/manual replay, lease recovery and safe operator actions in a feature-folder runbook; distinguish proposed thresholds from agreed operational objectives.
- [ ] Run `dotnet build` and `dotnet test` from `backend/`, exercise changed HTTP endpoints, and verify telemetry output renders correctly. Run SQL/Redis suites with the required services available.
- [ ] Report changed files, migration/configuration steps, concrete test results and any environment-dependent checks not executed. Do not call recovery acceptance complete while those checks remain unverified.

Done when plan sections 10 and 14 are demonstrated: SQL remains authoritative, accepted moves survive Redis outages, retries cannot duplicate occupancy/movements, clients converge after reconnect, and failures are observable and recoverable.

## Scope boundaries

Frontend owns views, dialogs, browser command identity, pending/error states, stream consumption and client reconciliation. Backend owns all clinical validation, persistence, authorization and committed publication. Administrative hierarchy editing, a generator dashboard and optional Redis read projections are not required first-release deliverables unless separately selected.

Do not silently substitute the current in-memory event bus for a durable outbox, or Redis locks for SQL concurrency protection. Deployment credentials, privacy/retention policy and operational objectives remain explicit configuration/review inputs from plan section 15; complete independent implementation work while those inputs are pending.
