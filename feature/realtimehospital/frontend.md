# Real-time hospital: frontend handoff for Luna

Status: implementation work breakdown; design decisions remain subject to [plan.md](plan.md).  
Owner: frontend implementer. Backend counterpart: [backend.md](backend.md).

## Outcome and scope

Build a hospital-to-bed occupancy view where authorized staff can move an admitted patient and all connected clients converge on committed backend state. Include location history, retry/conflict handling, and reconnect recovery. SQL-backed APIs own occupancy; the browser never connects to Redis.

Work in `frontend/`. Read `frontend/.agents/AGENTS.md` and the relevant local playbooks before implementation. Use the codebase knowledge graph for code discovery. Keep each chunk independently reviewable and report its verification before proceeding.

Current entry points:

- `src/App.tsx`: application navigation and composition.
- `src/components/Patients.tsx` and `Admissions.tsx`: links into the hospital view.
- `src/lib/api.ts`: bearer authentication, correlation IDs, errors, and request telemetry.
- `src/lib/auth.ts`: existing role helpers.
- `src/lib/redaction.ts` and `session-recorder.ts`: clinical-data recording restrictions.
- `src/index.css`: existing design tokens and UI primitives.

## Shared contract and sequencing

Start with backend chunk BE-01. The backend owns a shared `feature/realtimehospital/api-contract.md` with concrete JSON fixtures and OpenAPI definitions. This is a planned implementation deliverable, not an existing file. Build against those fixtures once published; changes affecting both sides must update them first.

Proposed first-release choices are SSE for live delivery and confirmed-state UI with a pending indicator. These are working recommendations, not already approved decisions from the source plan.

| Frontend dependency | Backend deliverable |
| --- | --- |
| FE-01 types and fixtures | BE-01 contract, error codes, permissions and transport choice |
| FE-02 layout/history | BE-03 snapshot and history endpoints |
| FE-03 staff movement | BE-04 movement endpoint and replayable result |
| FE-04 live reconciliation | BE-06 authenticated stream, replay and gap protocol |
| FE-05 integrated acceptance | BE-07 generator and BE-08 recovery environment |

The contract must distinguish an admission's concurrency version, its event sequence, the hospital stream cursor, and a generator run's sequence. An API result for one move must not advance the stream cursor past unseen events for other admissions. Agree the race-free snapshot-to-stream boundary, scope changes, and gap semantics before wiring live state.

## FE-01 — Types, fixtures and API transport

Prerequisite: BE-01; no dependency on running SQL or Redis for fixture-driven work.

- [ ] Add typed hierarchy, location, occupancy snapshot, movement command/result, movement history, event envelope and RFC 9457 error contracts.
- [ ] Include nullable/unlocated states where the agreed contract permits them; do not invent a bed for an admission whose current model only has a ward string.
- [ ] Add API functions for layout, locations, history and movement through the existing shared transport.
- [ ] Preserve a caller-supplied correlation ID for retries of one command. The current `api()` unconditionally replaces it; retain fresh IDs for unrelated requests.
- [ ] Preserve machine-readable conflict codes and current location/version from problem-details extensions. The current wrapper drops these fields.
- [ ] Prepare fixtures for empty/full/blocked beds, multiple hierarchy levels, redacted identity, inactive admissions, success, each conflict, duplicate events and cursor expiry.
- [ ] If SSE is selected, extend the shared API transport for authenticated streaming and parsing. Existing JSON-only handling needs a stream path. Do not put bearer tokens in URLs; native `EventSource` cannot attach the existing bearer header.

Acceptance: types and fixtures match BE-01; command headers survive retries; conflict context reaches the UI; shared authentication, correlation and redaction behavior remains intact. No new runtime package is expected.

## FE-02 — Hospital layout, occupancy and history

Prerequisite: FE-01. Use fixtures until BE-03 is available.

- [ ] Add navigation to the hospital view and links from the existing patient/admission lists.
- [ ] Render hospital, floor, ward, room and bed hierarchy with occupancy counts and available/occupied/blocked/inactive states from the API.
- [ ] Show the full current location path and only the patient display identity supplied for the user's permissions.
- [ ] Add admission movement history using the history endpoint, with times displayed consistently from UTC data.
- [ ] Handle loading, empty, unlocated, forbidden and failed-load states; show correlation IDs with errors.
- [ ] Show connection/freshness state and last event time. Snapshot-only operation must not look like a live connection.
- [ ] Use existing tokens, accessible controls, non-sensitive tracking names and region markers. Check narrow screens and keyboard navigation.

Acceptance: every seeded bed is reachable in the hierarchy; counts agree with the authoritative snapshot; existing patient registration and admit/discharge screens still work. No layout or patient values leak into session telemetry.

## FE-03 — Staff movement and safe retries

Prerequisite: FE-02 and BE-04 contract; complete live verification when BE-04 is available.

- [ ] Add a move dialog with current location, target hierarchy/bed selector, reason and confirmation. Gate controls using the agreed permissions; backend authorization remains decisive.
- [ ] Submit `admissionId`, `patientId`, `fromBedId`, `toBedId`, `reason`, `occurredAt`, `expectedVersion` and `clientMutationId` as defined in the contract.
- [ ] Generate one idempotency key and correlation ID per logical command. Retain the exact payload and IDs across a network timeout/retry, including the original occurrence time.
- [ ] Disable duplicate submission while pending. If the user changes a failed/conflicted command, construct a new command/key rather than reusing the old key with different data.
- [ ] Keep unresolved commands through dialog/view transitions within the authenticated session. Clear sensitive state on logout or user/scope changes; do not persist clinical payloads in browser storage by default.
- [ ] Show pending state until an authoritative response or contract-defined matching event confirms the move. Reconcile response/event arrival in either order without double-counting occupancy.
- [ ] On `409`, display the machine-readable reason as helpful text and refresh the affected admission/bed state. Never silently resubmit using a newer version.
- [ ] On authentication failure, validation failure or unknown resource, show the appropriate recovery action and correlation ID. Treat an ambiguous network failure as unresolved, not proof of rollback.

Acceptance: a repeated retry sends identical command identity and payload; conflicts show current backend state; only one committed transition appears even if the stream arrives before the HTTP response. Staff moves also work while Redis is unavailable, with live freshness clearly marked.

## FE-04 — Live events and reconnect reconciliation

Prerequisite: FE-01 and BE-06 protocol. Fixture-driven reducer/parser work can start earlier.

- [ ] Bootstrap an authoritative snapshot with its replay boundary, then connect using the agreed cursor protocol. Handle events committed between snapshot creation and subscription.
- [ ] Scope the connection to the authorized hospital; cancel old subscriptions and in-flight loads on scope change, logout or unmount.
- [ ] Deduplicate by `eventId`, enforce per-admission sequence ordering, and ignore older events/results that would regress location.
- [ ] Apply a move atomically across admission location, source/target bed occupancy and parent counts. If the event lacks required display data, fetch authoritative affected state.
- [ ] Maintain the last fully applied stream cursor separately from individual movement response cursors. Recover sequence gaps, expired cursors and unknown event schemas through the agreed replay/snapshot path.
- [ ] Reconnect with bounded exponential backoff and jitter; handle token expiry. Expose reconnecting/offline/stale states and a manual refresh action.
- [ ] After a gap, replace state with a fresh authoritative snapshot and resume at its boundary; avoid combining an old cursor with a newer partial snapshot.
- [ ] Keep effects safe under React StrictMode; prevent duplicate active streams and clean up readers, timers and abort controllers.

Acceptance: two clients converge after either submits a move; duplicate/out-of-order events do not change final occupancy; events around initial load and reconnect are not missed. Disconnect beyond retention, then reconnect and verify snapshot recovery.

## FE-05 — Verification and handoff

Prerequisite: FE-01 through FE-04. BE-07 is needed for generator/staff contention acceptance, not for the initial UI.

- [ ] Add focused behavioral tests for stable retries, problem-detail extensions, deduplication, ordering, response/event races, cursor gaps and stream cleanup using the repository's test approach.
- [ ] Run `npm run lint`, `npm run test` and `npm run build` from `frontend/`; build includes the bundle budget check.
- [ ] Open the UI in a real browser and exercise layout, keyboard movement, history, conflict feedback and redacted identity.
- [ ] With the backend available, use two sessions to test a normal move, a contested target, a lost-response retry, reconnect replay, expired-cursor recovery and Redis downtime/recovery.
- [ ] Exercise generator events alongside staff moves; both must render through the same committed event path.
- [ ] Report changed files, checks actually run, results, and any environment-dependent checks still outstanding. Fixture success alone is not integrated acceptance.

Done when the frontend satisfies the applicable acceptance criteria in plan sections 8, 10 and 14 and converges to the backend snapshot after failure recovery.

## Boundaries and unresolved decisions

Backend owns hierarchy migrations/backfill, all occupancy rules and locking, SQL persistence, idempotency/outbox, Redis, replay, permissions enforcement and the generator engine/control APIs.

The source plan requires admin-only generator APIs but does not explicitly require a generator dashboard. A dashboard is optional follow-up work; do not add it to the initial frontend scope. If requested, consume the backend run lifecycle endpoints and keep controls admin-only.

BE-01 records the transport choice, role/scope rules, identity redaction, timestamp rules, initial allocation semantics and freshness objectives. Do not infer permission to expose full identity from the existing broad `canWrite` helper. Continue fixture-driven work when policy choices are pending, clearly marking assumptions.
