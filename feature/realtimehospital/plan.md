# Real-time hospital patient movement

Status: design proposal for review  
Scope: patient location, movement events, live occupancy, and a deterministic movement simulator

## 1. Purpose

This feature will let staff see where admitted patients are in the hospital hierarchy and move them between beds through the frontend. The backend remains the authority for all state changes and records every accepted movement in SQL Server. A separate Redis server provides low-latency distribution of committed updates to connected frontend clients and supports worker coordination; Redis is not the system of record.

The hierarchy is:

```text
Hospital
└── Floor
    └── Ward
        └── Room
            └── Bed
                └── Patient location / bed stay
```

The design must support three event sources with the same command path:

1. a staff action from the frontend;
2. the backend movement generator for mock/demo traffic; and
3. future integrations or operational jobs.

All sources must be subject to the same validation, locking, idempotency, transaction, audit, and publication rules.

## 2. Existing model and implications

The first basis is:

- `backend/database/001_initial_hospital_schema.sql`
- `backend/database/002_seed_mock_data.sql`

The existing SQL model already provides useful foundations:

- `patients` and `admissions` identify the patient and active episode;
- `wards` and `beds` provide the current physical resource model;
- `bed_requests` and `bed_stays` represent allocation and occupancy history;
- `flow_events` is an append-only operational event log with a correlation ID and JSON payload;
- `simulation_scenarios`, `simulation_runs`, `simulation_events`, and metric samples separate simulation state from operational state;
- `concurrency_version` exists on several mutable aggregates;
- SQL Server triggers reject overlapping bed stays and invalid cross-entity relationships;
- the schema sets `XACT_ABORT ON` and the seed script uses a transaction.

The current application surface is smaller than the SQL model: the frontend currently lists/registers patients and admits/discharges them, while the existing domain admission object carries a ward string rather than a room/bed location. The realtime feature should therefore introduce a location contract and persistence boundary rather than extend the current string field informally.

The current schema does not have `hospitals`, `floors`, or `rooms`. The first database migration for this feature must add those entities and make the bed hierarchy explicit. A bed should belong to a room, a room to a ward, a ward to a floor, and a floor to a hospital. Existing seeded wards and beds need a deterministic backfill.

## 3. Target architecture

```text
                           ┌──────────────────────────────┐
                           │ SQL Server                    │
                           │ authoritative state + audit   │
                           │ outbox / idempotency records  │
                           └──────────────┬───────────────┘
                                          │ same DB transaction
                         command          │ committed outbox row
┌──────────────┐   HTTP  │       ┌────────▼─────────┐
│ React client │──────────┼──────▶│ Movement API     │
│ map + list   │          │       │ validation/use   │
└──────┬───────┘          │       │ case + publisher │
       │ WebSocket/SSE    │       └────────┬─────────┘
       │                                  │
       │                         ┌────────▼─────────┐
       └─────────────────────────│ Redis server     │
                                 │ Streams + pubsub │
                                 │ live projections │
                                 └────────┬─────────┘
                                          │ consumer groups
                                  ┌───────▼────────┐
                                  │ Outbox relay /  │
                                  │ projection      │
                                  │ workers         │
                                  └────────────────┘

                    ┌──────────────────────────────┐
                    │ Movement generator           │
                    │ deterministic worker/client  │
                    │ sends normal movement cmds   │
                    └──────────────┬───────────────┘
                                   └── HTTP/command bus
```

### Responsibilities

| Component | Responsibility | Must not do |
| --- | --- | --- |
| Frontend | Render an initial authoritative snapshot, submit commands, subscribe to committed movement events, reconcile optimistic state | Decide whether a bed is available or write Redis directly |
| Movement API | Authenticate/authorize, validate commands, execute the atomic SQL transaction, return the authoritative result | Treat a Redis write as proof that a movement succeeded |
| SQL Server | Own hierarchy, admissions, active location, immutable movement/audit history, idempotency, and durable outbox | Depend on Redis availability to commit a movement |
| Outbox relay | Read committed outbox rows and publish them to Redis with retry and acknowledgement | Create a second source of truth |
| Redis server | Buffer/fan out committed events and optionally hold short-lived read projections | Be the only copy of movement history or authoritative occupancy |
| Movement generator | Produce reproducible commands for a scenario/run and record run state | Mutate beds with a private code path that bypasses movement rules |

Redis is a separate server/process, with its own persistence, monitoring, authentication, TLS policy, and resource limits. It may be unavailable without preventing an accepted movement from being committed to SQL Server.

## 4. Domain and persistence model

### 4.1 Hierarchy

Add a migration with tables similar to:

```text
hospitals(id, code, name, ...)
floors(id, hospital_id, code, name, ...)
wards(id, floor_id, code, name, ward_type, ...)
rooms(id, ward_id, code, name, ...)
beds(id, room_id, code, bed_type, ...)
```

Recommended invariants:

- codes are unique within their parent (`hospital.code`, `(hospital_id, floor.code)`, `(floor_id, ward.code)`, `(ward_id, room.code)`, `(room_id, bed.code)`);
- every ancestor is active before a child can be used;
- a bed belongs to exactly one room at a point in time;
- a move may only target a bed in the requested hospital hierarchy and, unless explicitly configured otherwise, the same active admission;
- hierarchy changes are administrative operations and are not represented as patient movements;
- existing `wards` are backfilled to one hospital/floor and existing beds to deterministic rooms, documented in the migration.

If a physical bed can be shared across rooms or wards in a future deployment, that should be a separate design decision. The first implementation should keep the ownership chain strict.

### 4.2 Active location and history

Reuse `bed_stays` as the durable movement history, with the following target semantics:

- one open bed stay per admission;
- one open bed stay per bed;
- half-open intervals `[started_at, ended_at)` so a transfer at time `T` can end one stay and start the next at `T`;
- the stay contains the source/target context needed for audit, either through the prior row or explicit `from_bed_id`/`to_bed_id` movement fields;
- current location is derived from the open stay, not from Redis;
- the response includes the full location path: hospital, floor, ward, room, and bed IDs/codes.

The existing overlap trigger protects historical intervals, but the command transaction must also lock the active source and target rows before checking availability. Database constraints/triggers remain the final guard against races.

Consider adding a dedicated `patient_movements` table rather than overloading `flow_events` for command identity and queryability:

```text
patient_movements(
  id, admission_id, patient_id,
  from_bed_id, to_bed_id,
  reason, source, occurred_at, recorded_at,
  correlation_id, idempotency_key, payload_json,
  created_by, status/version
)
```

It should be append-only after acceptance. `flow_events` can remain the general event/audit stream, with a `patient.moved` event referencing the movement ID. If the review prefers one table, `flow_events` needs a first-class `event_id`/`idempotency_key`, patient and location references, and indexes that make current-location and movement-history queries efficient.

## 5. Commands and events

### 5.1 Move command

The frontend and generator submit the same logical command:

```json
{
  "admissionId": "uuid",
  "patientId": "uuid",
  "fromBedId": "uuid",
  "toBedId": "uuid",
  "reason": "clinical-transfer",
  "occurredAt": "2026-09-12T04:00:00Z",
  "expectedVersion": 7,
  "clientMutationId": "uuid"
}
```

Transport metadata:

- `Idempotency-Key`: stable UUID generated once per user/generator command;
- `X-Correlation-Id`: request/trace ID, stable across retries of the same attempt chain;
- authenticated actor and source (`frontend`, `generator`, `integration`);
- server records `receivedAt` and uses UTC.

`patientId` and `fromBedId` are not trusted merely because the client sent them. The backend loads the admission and active stay and verifies that they match the current state. `expectedVersion` enables optimistic concurrency for stale screens; the locked SQL check remains authoritative.

### 5.2 Result semantics

- `201 Created` or `200 OK`: first successful application, with authoritative movement and location.
- Same idempotency key plus the same canonical request hash: return the original success result; do not create another stay or event.
- Same idempotency key plus a different request hash: `409 Conflict`.
- stale `expectedVersion`: `409 Conflict` with the current version/location so the frontend can reload.
- unavailable/occupied/blocked bed: `409 Conflict` with a machine-readable reason.
- unknown patient/admission/bed: `404 Not Found`.
- invalid command: `400`/`422` using the existing RFC 9457 problem-details convention.

### 5.3 Committed event envelope

Publish only after the SQL transaction commits:

```json
{
  "eventId": "uuid",
  "eventType": "patient.moved.v1",
  "aggregateType": "admission",
  "aggregateId": "uuid",
  "sequence": 12,
  "occurredAt": "2026-09-12T04:00:00Z",
  "recordedAt": "2026-09-12T04:00:01Z",
  "correlationId": "uuid",
  "causationId": "uuid",
  "source": "frontend",
  "payload": {
    "patientId": "uuid",
    "from": { "hospitalId": "uuid", "floorId": "uuid", "wardId": "uuid", "roomId": "uuid", "bedId": "uuid" },
    "to": { "hospitalId": "uuid", "floorId": "uuid", "wardId": "uuid", "roomId": "uuid", "bedId": "uuid" },
    "reason": "clinical-transfer"
  }
}
```

The event is a notification of durable state. Consumers must be able to replay it and must fetch a snapshot when they detect a gap. Event schema versions are additive and must be explicitly versioned.

## 6. Atomic backend workflow

The move use case should execute the following in one SQL transaction with a bounded retry policy for transient SQL deadlocks/timeouts:

1. Validate authentication, authorization, request shape, idempotency key, and canonical request hash.
2. Start a transaction with `XACT_ABORT ON`.
3. Insert or lock the idempotency record by `(scope, idempotency_key)`.
4. Lock the admission, current open stay/source bed, target bed, and relevant hierarchy/capacity rows in a deterministic order (`admission`, lower bed ID, higher bed ID, ancestors).
5. Confirm the admission is active, the source matches the current open stay, the target is active and not blocked/retired, and the target has no overlapping open stay.
6. Check `expectedVersion` and capacity/business rules.
7. Close the source stay at `occurredAt` and insert the target stay/movement, incrementing the aggregate concurrency version exactly once.
8. Append the immutable `patient.moved`/`flow_events` record.
9. Insert an outbox record containing the complete event envelope and a deterministic event ID.
10. Store the response body/status on the idempotency record, mark it completed, and commit.
11. Return the committed response. The outbox relay publishes asynchronously.

No Redis call belongs inside this transaction. If any step fails, SQL rolls back all state, movement, audit, outbox, and idempotency changes. A retry may safely re-run the same command.

### Idempotency record

Use a durable table, for example:

```text
command_idempotency(
  scope, idempotency_key, request_hash,
  status, response_status, response_json,
  resource_id, created_at, completed_at, expires_at,
  primary key(scope, idempotency_key)
)
```

The request hash must be calculated from a canonical representation that excludes transport-only headers. The key is scoped by tenant/hospital and command type as appropriate. Keep completed records for at least the client retry window and retain an audit-safe reference after expiry if policy requires it.

Do not rely only on client-generated IDs, Redis `SETNX`, or a preflight availability query: none of those alone protects the SQL write from retries or races.

## 7. Outbox relay and Redis design

### 7.1 Outbox

The outbox row is committed with the movement. Suggested fields are `id`, `event_id`, `aggregate_id`, `event_type`, `payload_json`, `created_at`, `published_at`, `attempt_count`, `next_attempt_at`, `last_error`, and a status. Add a unique constraint on `event_id`.

The relay claims a small batch using SQL Server row locks/lease fields, publishes to a Redis Stream, and marks rows published only after Redis acknowledges the write. Claim leases expire so a crashed relay can recover work. A publish timeout must leave the row retryable; it must never mark the row complete before acknowledgement.

Redis Stream recommendation:

- stream: `hospital:{hospitalId}:patient-movements:v1`;
- consumer group: `realtime-projections` for durable projection workers;
- separate consumer group or a fan-out bridge for WebSocket/SSE delivery;
- fields include `eventId`, `aggregateId`, `sequence`, `eventType`, `schemaVersion`, and `payload`;
- acknowledge only after a consumer has durably applied the event or safely handed it to connected clients;
- configure a pending-entry recovery process (`XAUTOCLAIM` or equivalent), dead-letter stream, max stream length/retention, and monitoring.

Redis Pub/Sub may be used only as a best-effort wake-up signal. A browser reconnect must get missed events from SQL or the Redis Stream, then receive live events. Pub/Sub alone loses messages during disconnects.

### 7.2 Redis projections

Optionally maintain short-lived keys for low-latency reads:

```text
hospital:{id}:snapshot:{version}
hospital:{id}:bed:{bedId}
hospital:{id}:ward:{wardId}:occupancy
```

Projection updates must be idempotent by `eventId` and monotonic by per-aggregate `sequence`. They should be rebuilt from SQL/current snapshot plus the stream after loss or corruption. Set TTLs and avoid storing unnecessary patient-identifying data in Redis.

## 8. Frontend behaviour

### Initial load

1. Authenticate as today.
2. Request a hierarchy snapshot endpoint, for example `GET /api/hospital-layout`.
3. Request current locations and a cursor/version, for example `GET /api/patient-locations?since=...`.
4. Open SSE or WebSocket with the hospital scope and last applied event cursor.
5. If the server reports a cursor gap, refetch the snapshot and restart from the returned cursor.

The screen should render the hierarchy from hospital to bed and show occupancy, patient display identity permitted for the role, stale/offline state, and the last event time. The existing patient/admission lists can link into this view.

### Moving a patient

- Disable the move action while the same command is pending, but allow safe retry after a network timeout using the same `Idempotency-Key`.
- Optimistic UI is optional; if used, mark the move as pending and never present it as committed until the API response or matching event arrives.
- On success, replace local state with the API's authoritative result and record the returned event cursor.
- On `409`, show the current location/reason and refresh the affected admission/bed rather than guessing.
- On network failure, keep the command ID and idempotency key so retrying cannot duplicate the movement.
- On reconnect, send the last applied cursor and reconcile from the snapshot/gap response.

The frontend must not update another client's screen by emitting a local event. It should update from the backend event stream, which gives all clients the same committed ordering.

## 9. Movement generator

Implement the generator as a hosted worker or separately deployable worker using the existing simulation concepts:

- scenario and version are immutable;
- each run has a random seed, engine version, snapshot, status, and deterministic sequence;
- the generator records its generated command/event identity and run/sequence number;
- it submits movements through the same application command handler/API, with `source=generator`;
- a dry-run mode validates and reports conflicts without committing;
- pause, resume, cancel, rate limit, and retry are explicit run operations;
- generator retries reuse the same idempotency key for the same generated command;
- failed commands are recorded with reason and do not silently advance the scenario sequence;
- a run can resume from its last durable sequence after process failure.

The generator must not use Redis as a work queue without a durable run record. A generated event may be published to the same stream as staff movements, but the envelope must retain `source`, `runId`, and `sequence` for filtering and analysis.

## 10. Failure and recovery matrix

| Failure point | Expected result | Recovery |
| --- | --- | --- |
| Validation fails | No SQL/Redis mutation | Return problem details |
| SQL transaction deadlock/timeout | Full rollback | Retry bounded times with same idempotency key; then return retryable error |
| Process dies before SQL commit | Nothing committed | Client/generator retries the same command |
| Process dies after SQL commit before response | Movement, idempotency, and outbox exist | Retry returns stored response; relay publishes event |
| Redis is down | SQL movement still commits; outbox remains pending | Relay retries with backoff; alert on age/lag |
| Relay dies after Redis acknowledgement before SQL mark | Possible duplicate publish | Consumers deduplicate by `eventId`; relay safely retries |
| Consumer dies after reading | Pending Stream entry remains | Claim/retry; deduplicate projection by event ID/sequence |
| Browser disconnects | No data loss in SQL/outbox/Stream | Reconnect with cursor; replay or refresh snapshot |
| Redis data loss | No loss of authoritative state | Rebuild projection from SQL and replay outbox/history |
| Generator dies mid-run | Run remains `running`/lease-expired | Resume from last durable sequence or mark failed; never regenerate with new keys |
| Two moves target one bed | One commits, one conflicts | Deterministic locks plus database constraints; loser reloads |
| Same idempotency key, changed payload | No second mutation | Return `409 IdempotencyKeyReuse` |

Use exponential backoff with jitter, bounded attempts, and a dead-letter/manual-replay path. Do not retry permanent validation or business conflicts.

## 11. API surface (proposed)

```text
GET  /api/hospital-layout
GET  /api/patient-locations?hospitalId=...
GET  /api/patient-locations/{admissionId}/history
POST /api/patient-movements
GET  /api/realtime/patient-movements   (SSE) or WebSocket endpoint
POST /api/simulation/runs
POST /api/simulation/runs/{runId}/pause
POST /api/simulation/runs/{runId}/resume
POST /api/simulation/runs/{runId}/cancel
GET  /api/simulation/runs/{runId}
```

All write endpoints accept correlation and idempotency headers, enforce role permissions, and return RFC 9457 problem details with a correlation ID. Admin-only generator controls must not be exposed as general staff actions.

## 12. Security and privacy

- Authorize by hospital/ward scope and role; never trust a hospital ID from the browser without checking claims.
- Return the minimum patient identity required by the user's role. Prefer display name/MRN redaction rules already used by the frontend.
- Encrypt API-to-Redis traffic and require Redis authentication/ACLs; keep Redis on a private network.
- Do not put full patient demographics in Redis stream fields, logs, telemetry, or exception messages.
- Audit actor, source, reason, correlation ID, causation ID, and timestamps for every accepted/rejected movement as policy permits.
- Apply retention, access, and deletion rules to event payloads; append-only does not mean unrestricted access.

## 13. Delivery slices

### Slice 1: durable hierarchy and movement core

- Add hospital/floor/room tables and backfill seed data.
- Add a typed location read model and repository.
- Add movement transaction, `patient_movements` or upgraded `flow_events`, idempotency table, and outbox.
- Add concurrency/overlap tests and API contract tests.

### Slice 2: frontend layout and staff moves

- Add hierarchy snapshot/current-location endpoints.
- Add bed/room/ward/floor/hospital view and move dialog.
- Add idempotent retry, stale conflict handling, and reconnect reconciliation.

### Slice 3: Redis and live delivery

- Provision separate Redis server and credentials.
- Implement relay, Stream groups, deduplication, pending-entry recovery, and SSE/WebSocket delivery.
- Add lag, pending count, outbox age, reconnect, and dead-letter dashboards/alerts.

### Slice 4: deterministic generator

- Implement run lifecycle, seeded movement generation, dry run, pause/resume/cancel, and resume-after-failure.
- Exercise generator and staff commands concurrently against the same rules.

## 14. Verification plan

### Unit and domain tests

- valid moves produce one closed stay, one open stay, one movement, one audit event, and one outbox row;
- source mismatch, inactive admission, blocked/retired target, wrong hierarchy, and capacity violations are rejected;
- duplicate command retries return the original result without new records;
- idempotency-key reuse with a changed payload conflicts;
- concurrent moves cannot occupy one bed or leave one admission in two beds;
- transfer at the exact end/start timestamp is allowed, overlapping intervals are rejected;
- generator replay with the same seed and version produces the same command identities/order.

### Integration and failure tests

- kill the API after SQL commit and before response, then retry;
- force a deadlock/timeout and verify rollback plus safe retry;
- stop Redis and verify writes still commit and outbox lag recovers;
- kill the relay after Redis acknowledgement and verify consumer deduplication;
- kill a consumer and verify pending-entry reclamation;
- delete/recreate Redis projection data and rebuild from SQL;
- disconnect/reconnect a browser and verify cursor replay or snapshot recovery;
- run generator and frontend moves concurrently under contention.

### Operational acceptance criteria

- SQL is always sufficient to reconstruct current location and history;
- no accepted movement is lost when Redis is unavailable;
- no command retry creates duplicate occupancy or duplicate logical movement;
- every live client converges to the same authoritative snapshot after reconnect;
- outbox, Redis, consumer, and generator failures are visible through metrics and structured logs;
- recovery procedures are documented and exercised before production use.

## 15. Review decisions required

1. Should movement history be a dedicated `patient_movements` table, or should `flow_events` be extended?
2. Should live delivery use SSE or WebSockets for the first release? SSE is simpler if the browser only receives events.
3. What is the canonical hospital/floor/room backfill for the existing seed wards and beds?
4. What event retention and patient-data redaction policy applies to SQL, Redis, logs, and dead-letter records?
5. Which roles may move patients, view full identity, operate the generator, and replay failed events?
6. What latency and recovery objectives should determine Redis retention, outbox retry limits, and alert thresholds?

