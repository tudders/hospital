# Real-time hospital: frontend implementation and handoff

Status: read-only 3D occupancy view implemented and verified against the configured database on 2026-09-13 using the development TLS setting below; movement and live-event work remains planned under [plan.md](plan.md).

Branch: `realtimehosptital`.

Owner: frontend implementer. Backend counterpart: [backend.md](backend.md).

## Outcome and scope

The delivered slice is a React page showing a 3D hospital model with point-in-time occupancy on hover. The requested hierarchy is **5 floors, 4 wards per floor, 6 rooms per ward and 6 beds per room**: 20 wards, 120 rooms and 720 beds. SQL-backed snapshots own occupancy; the browser never connects directly to SQL or Redis.

The broader roadmap remains a hospital-to-bed occupancy view where authorized staff can move an admitted patient and all connected clients converge on committed backend state, with location history, retry/conflict handling and reconnect recovery. Those movement and streaming capabilities are not implemented by this slice.

Work in `frontend/`. Read `frontend/.agents/AGENTS.md` and the relevant local playbooks before implementation. Use the codebase knowledge graph for code discovery. Keep each chunk independently reviewable and report its verification before proceeding.

Current entry points:

- `src/App.tsx`: application navigation and composition; hospital overview is the default page after sign-in.
- `src/components/Hospital.tsx`: snapshot loading, time/source controls, polling, occupancy summaries and hierarchy explorer.
- `src/components/HospitalModel.tsx`: SVG projection of 3D bed/floor geometry, hover inspection, bed selection and camera controls.
- `src/lib/hospital.ts`: typed bed/snapshot models, hierarchy grouping, occupancy counts and explicit sample data.
- `src/lib/hospital.test.ts`: hierarchy reachability, counts, empty/inactive inventory and deterministic sample checks.
- `src/components/Patients.tsx` and `Admissions.tsx`: links into the hospital view.
- `src/lib/api.ts`: bearer authentication, correlation IDs, errors and request telemetry; query values are excluded from telemetry and cancelled requests do not produce network-error events.
- `src/lib/auth.ts`: existing role helpers.
- `src/lib/redaction.ts` and `session-recorder.ts`: clinical-data recording restrictions.
- `src/index.css`: existing design tokens and UI primitives.

## Delivered 3D occupancy slice

- Exploded and compact hospital views, individual-floor views, rotation and camera reset. The renderer projects 3D coordinates into SVG and adds no frontend runtime dependency.
- Hover inspection at floor, ward, room and bed levels, showing occupancy and the snapshot timestamp. Clicking a bed selects its floor, ward, room and bed in the explorer.
- Keyboard-accessible floor/ward selectors and room/bed buttons, plus a responsive layout checked at 390px width.
- Occupied, available, blocked and inactive bed states; parent counts derive from the same complete snapshot. Occupancy percentage uses active physical inventory (`total - inactive`), not staffed capacity.
- A local-time timestamp picker sends an encoded UTC timestamp to `GET /api/hospital-occupancy?at=...`. Omitting `at` requests the server's current time. The UI displays the snapshot time, capture time and browser timezone.
- Current snapshots optionally refresh 15 seconds after each request completes. Historical snapshots do not poll. Freshness labels distinguish polling, paused, historical, sample and unavailable/stale states; there is no SSE subscription or event cursor.
- Loading, empty-inventory and failed-load screens, correlation IDs on API errors, manual refresh and retention of the last complete snapshot after a failed refresh. Effects cancel requests and timers on source/time changes, navigation and logout.
- Explicitly selected sample mode with 720 synthetic beds, including blocked/inactive examples. Sample occupancy varies deterministically with the selected hour. Sample data is visibly labelled and never substituted silently or merged into SQL results.
- Static tracking names and region markers. No patient identity or admission identifier is included in the occupancy response or inspector.

The SQL hierarchy already exists in `backend/database/003_expand_full_hospital_mock_data.sql`. The API reads that hierarchy and the bed-stay/block intervals through `HospitalOccupancyController` and `HospitalOccupancyReader`; SQL responses are grouped by their actual IDs without substituting generated sample beds. Only beds attached to the complete hierarchy are returned. Ward-string admissions are not assigned invented beds.

Historical occupancy uses half-open intervals `[start, end)` with the current physical hierarchy. The schema does not preserve historical room/floor assignments. An available bed is unoccupied and unblocked; staffing and clinical suitability are outside this view.

### Database configuration and verified development connection

In Development, the API reads `SQL_SERVE_CONNECTION_STRING` from the existing `frontend/.env`, including its legacy `ConnectionString=` wrapper. Hosted configuration uses `ConnectionStrings__Hospital` or `SQL_SERVE_CONNECTION_STRING` on the API process. Database settings must never use a `VITE_` prefix. The existing `.env` was not modified.

The updated connection string authenticates successfully, and effective `SELECT` permission is now granted on all seven required tables: `dbo.hospitals`, `dbo.floors`, `dbo.wards`, `dbo.rooms`, `dbo.beds`, `dbo.bed_stays` and `dbo.bed_blocks`. The password and table-access blockers are resolved. No external data or permissions were modified during verification.

The SQL server certificate is not trusted by the API driver: the ordinary connection produced SQL error `-2146893019` and HTTP 503. End-to-end verification succeeded with `Encrypt=True;TrustServerCertificate=True` supplied only to a separate Development API process. Encryption remains enabled, but that process does not validate the server certificate. The saved `.env` and production configuration were not changed. A normally launched API still needs a trusted server certificate or an explicitly chosen development trust setting.

The working preview uses frontend `http://localhost:5173` and API `http://localhost:5026`. To reproduce it after building the API, run `./feature/realtimehospital/start-preview-api.ps1 -TrustDevelopmentCertificate` from the repository root. In another PowerShell terminal, run `$env:VITE_API_URL = 'http://localhost:5026'` followed by `npm run dev -- --host localhost --port 5173` from `frontend/`. The script reads the existing credential without printing or saving it and restores its process environment when the API stops. Its certificate override requires the explicit switch.

[grant-occupancy-read.sql](grant-occupancy-read.sql) remains an optional DBA setup template for other accounts; no additional grants are needed for this verified connection. Earlier LocalDB verification is retained as separate evidence; its temporary database was removed.

See [api-contract.md](api-contract.md) for the implemented response shape, status semantics, authorization and configuration, and [verification.md](verification.md) for the evidence and limitations.

## Shared contract and sequencing

[api-contract.md](api-contract.md) now exists for the read-only `GET /api/hospital-occupancy` endpoint, with a concrete JSON example and a generated OpenAPI schema at `/openapi/v1.json` in Development. It does not complete BE-01 for movement, admission location/history or live-event contracts. Update the shared contract before making changes that affect both sides.

The implemented view uses complete SQL snapshots and optional polling. SSE for future live delivery and confirmed-state movement UI with a pending indicator remain working recommendations from the source plan.

| Frontend dependency | Backend deliverable |
| --- | --- |
| FE-01 types and fixtures | Bed/snapshot types and sample preview delivered; BE-01 still needed for movement/events |
| FE-02 layout/history | SQL occupancy endpoint delivered; admission location/history endpoints still needed |
| FE-03 staff movement | BE-04 movement endpoint and replayable result |
| FE-04 live reconciliation | BE-06 authenticated stream, replay and gap protocol |
| FE-05 integrated acceptance | BE-07 generator and BE-08 recovery environment |

The contract must distinguish an admission's concurrency version, its event sequence, the hospital stream cursor, and a generator run's sequence. An API result for one move must not advance the stream cursor past unseen events for other admissions. Agree the race-free snapshot-to-stream boundary, scope changes, and gap semantics before wiring live state.

## FE-01 — Types, fixtures and API transport

Read-only bed/snapshot types and sample preview are implemented. Remaining location, movement and event work depends on BE-01; fixture-driven work need not depend on running SQL or Redis.

- [x] Add typed bed hierarchy and occupancy snapshot contracts in `src/lib/hospital.ts`; use the existing RFC 9457 error transport.
- [ ] Add admission location, movement command/result, movement history and event envelope contracts.
- [ ] Include nullable/unlocated states where the agreed contract permits them; do not invent a bed for an admission whose current model only has a ward string.
- [x] Load the complete hierarchy and occupancy snapshot from `/api/hospital-occupancy` through `api()`.
- [ ] Add admission location, history and movement API functions through the existing shared transport.
- [x] Exclude snapshot query values from API telemetry and avoid network-error events for deliberately aborted requests.
- [ ] Preserve a caller-supplied correlation ID for retries of one command. The current `api()` unconditionally replaces it; retain fresh IDs for unrelated requests.
- [ ] Preserve machine-readable conflict codes and current location/version from problem-details extensions. The current wrapper drops these fields.
- [x] Add an explicit sample hospital with the complete 720-bed hierarchy and occupied/available/blocked/inactive bed examples; test empty and inactive inventory counts.
- [ ] Prepare admission identity, unlocated/inactive admission, movement success/conflict, duplicate-event and cursor-expiry fixtures.
- [ ] If SSE is selected, extend the shared API transport for authenticated streaming and parsing. Existing JSON-only handling needs a stream path. Do not put bearer tokens in URLs; native `EventSource` cannot attach the existing bearer header.

Acceptance for the remaining work: types and fixtures match BE-01; command headers survive retries; conflict context reaches the UI; shared authentication, correlation and redaction behavior remains intact. The delivered slice adds no frontend runtime package; the SQL reader adds Microsoft.Data.SqlClient 6.1.7 to the backend.

## FE-02 — Hospital layout, occupancy and history

The read-only layout and occupancy portion is delivered. Admission location/history remains dependent on the remaining FE-01 and BE-03 contracts.

- [x] Add navigation to the hospital view and links from the existing patient/admission lists.
- [x] Render hospital, floor, ward, room and bed hierarchy with occupancy counts and available/occupied/blocked/inactive states from the API.
- [x] Add exploded/compact 3D views, floor selection, rotation/reset, hover occupancy and a keyboard-accessible hierarchy explorer.
- [x] Add point-in-time snapshot selection, local-time display and optional current-snapshot polling.
- [ ] Show the full current location path and only the patient display identity supplied for the user's permissions.
- [ ] Add admission movement history using the history endpoint, with times displayed consistently from UTC data.
- [x] Handle loading, empty inventory and failed loads; show correlation IDs with API errors and retain the last complete snapshot as stale after a refresh failure.
- [ ] Add unlocated-admission presentation and verify forbidden/scope-specific recovery once those contracts exist.
- [x] Show snapshot/polling freshness, snapshot time and capture time without implying a live connection.
- [ ] Show last event time once event delivery is implemented.
- [x] Use existing tokens, accessible controls, non-sensitive tracking names and region markers. Check narrow screens and keyboard navigation.

Read-only verification: every seeded bed is reachable, SQL counts agree with the local seeded snapshot, and patient/admission navigation and links remain accessible. Snapshot query values were checked against captured telemetry payloads. Registration and admit/discharge mutation flows were not re-exercised in the browser for this slice; they remain part of broader acceptance alongside admission location/history.

## FE-03 — Staff movement and safe retries

Prerequisite: FE-02 and BE-04 contract; complete live verification when BE-04 is available.

Status: not implemented in the read-only 3D slice.

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

Status: no live stream, cursor or event reconciliation is implemented. Current polling effects clean up requests and timers, but that does not satisfy the stream-recovery requirements below.

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

- [x] Add focused tests for hierarchy reachability, parent counts, multiple hospitals, empty/inactive inventory and deterministic sample occupancy.
- [ ] Add focused behavioral tests for stable retries, problem-detail extensions, deduplication, ordering, response/event races, cursor gaps and stream cleanup using the repository's test approach.
- [x] Run `npm run lint`, `npm run test` and `npm run build` from `frontend/`; build includes the bundle budget check.
- [x] Open the UI in a real browser and exercise 3D layout, hover/bed selection, keyboard room/bed navigation, camera controls, existing page navigation and mobile layout.
- [x] Exercise the real SQL endpoint against the full local seed, including exact stay-end and block-start/end boundaries.
- [x] Verify SQL snapshots and the browser against the configured external database after its table read permissions are granted, using the explicit development certificate-trust setting documented above.
- [ ] Exercise staff movement, history, conflict feedback and role-redacted identity in a real browser once implemented.
- [ ] With the backend available, use two sessions to test a normal move, a contested target, a lost-response retry, reconnect replay, expired-cursor recovery and Redis downtime/recovery.
- [ ] Exercise generator events alongside staff moves; both must render through the same committed event path.
- [x] Report delivered files, checks actually run and outstanding environment-dependent checks in this handoff and [verification.md](verification.md). Local seed success alone is not external-server or realtime acceptance.

Recorded verification on 2026-09-13:

- Frontend lint and build passed; all 14 frontend tests passed. JavaScript was approximately 75.24 kB gzip against 90 kB, and CSS 3.20 kB against 8 kB.
- All 75 backend tests passed: 24 domain and 51 API tests, including occupancy authentication, invalid/future timestamps, unavailable-database errors, correlation headers and no-store behavior.
- At `2026-09-12T06:00:00Z`, the migrated local SQL database returned 720 beds, 144 per floor, 480 occupied and 240 available. Before inventory existed, all 720 were inactive.
- Chromium desktop and 390px mobile views were inspected, with no horizontal overflow on mobile. Sample mode and database-failure presentation were checked separately.
- External verification after the access update confirmed the full 1 hospital / 5 floors / 20 wards / 120 rooms / 720 beds hierarchy. Seed-time and current API snapshots returned HTTP 200 with 480 occupied and 240 available beds; a pre-inventory historical snapshot returned 720 inactive beds. Responses used `source: "sql"` and `Cache-Control: no-store`.
- Eleven real-browser checks passed directly against the external SQL-backed API, without response mocks: SQL source/counts, historical time selection, 144-bed floor view, hover inspection, 3D bed selection, keyboard selection, 390px mobile overflow, automatic polling to a newer snapshot, and absence of UI error alerts. Desktop/mobile screenshots were inspected. These checks used a separate Development process with encrypted transport and the explicit certificate-validation override described above.
- An existing cross-origin telemetry `sendBeacon` CORS error was observed against the API build used for browser checks. Separate telemetry work was in progress in the shared workspace; see the verification notes for that limitation.

Changed feature files are the entry points listed above, `frontend/.env.example`, the backend `Hospital/HospitalSnapshot.cs` and `Hospital/HospitalOccupancyReader.cs`, `Controllers/HospitalOccupancyController.cs`, the service registration and unavailable-error mapping, the SQL client package reference, `HospitalOccupancyTests.cs`, and the shared contract/verification documents. Other schema and telemetry edits already present or made concurrently in the shared workspace are not attributed to this slice.

The full frontend roadmap is done when it satisfies the applicable acceptance criteria in plan sections 8, 10 and 14 and converges to the backend snapshot after failure recovery. The delivered read-only view does not complete that roadmap.

## Boundaries and unresolved decisions

Backend owns hierarchy migrations/backfill, all occupancy rules and locking, SQL persistence, idempotency/outbox, Redis, replay, permissions enforcement and the generator engine/control APIs.

The source plan requires admin-only generator APIs but does not explicitly require a generator dashboard. A dashboard is optional follow-up work; do not add it to the initial frontend scope. If requested, consume the backend run lifecycle endpoints and keep controls admin-only.

The implemented occupancy endpoint allows any authenticated application user to read physical occupancy and returns no patient identities. Hospital-specific authorization is not yet modeled. BE-01 must still record movement/live transport, role/scope rules, identity redaction, initial allocation semantics and freshness objectives. Do not infer permission to expose full identity from the existing broad `canWrite` helper. Continue fixture-driven work when policy choices are pending, clearly marking assumptions.
