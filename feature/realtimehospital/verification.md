# 3D hospital view verification

Branch: `realtimehosptital`. Scope: read-only occupancy page and SQL snapshot endpoint.

- Frontend lint, 14 tests and production build pass. JavaScript is approximately 75 kB gzip
  against the existing 90 kB budget; CSS is approximately 3.2 kB against 8 kB.
- All 75 backend tests pass (24 domain and 51 API tests). HTTP tests cover anonymous denial, invalid/future timestamps, unavailable database
  problem details, correlation headers and no-store responses.
- A temporary SQL Server LocalDB database was created and all three repository migrations
  applied successfully. The actual API returned 720 beds, five floors of 144 beds, 480 occupied
  and 240 available at `2026-09-12T06:00:00Z`. Before inventory existed, all 720 were inactive.
- SQL interval boundary checks: an ended stay was occupied one tick before its end and available
  at its end; a block was available one tick before its start, blocked at its start, and available
  at its end.
- A real Chromium browser exercised the local SQL endpoint through the UI: all 720 bed meshes,
  single-floor 144-bed view, hover details, 3D selection, room/bed keyboard activation, rotation,
  reset, existing patient/admission navigation, and the link back into the hospital overview.
- Desktop and 390px mobile screenshots were inspected. Mobile has no horizontal overflow.
  Snapshot query values were checked against captured telemetry request payloads.
- Sample mode was verified separately, including blocked/inactive bed inspection. The database
  unavailable screen shows a correlation ID and never silently substitutes sample occupancy.

External verification completed on 2026-09-13 after the connection string and permissions were
updated by the owner. Authentication succeeds and effective SELECT permission is 1 on all seven
required tables. Direct SQL reads confirmed 1 hospital, 5 floors, 20 wards, 120 rooms and 720 beds.
No external data or permissions were modified during these checks.

The ordinary API connection still failed certificate validation with SQL error `-2146893019`.
A separate Development API process on port 5026 used the existing credential plus process-only
`Encrypt=True;TrustServerCertificate=True`. This retains encryption but bypasses server certificate
validation for the development connection; it does not prove a trusted-certificate production
connection. The saved `.env` and production configuration were not modified. Normal launches need
a trusted server certificate or an explicit development trust setting.

Against that process and the configured external database:

- Seed-time (`2026-09-12T06:00:00Z`) and current snapshots returned HTTP 200, `source: "sql"`,
  720 beds, 480 occupied and 240 available. Each floor had 144 beds, each ward 36 and each room 6.
- `2020-01-01T00:00:00Z` returned 720 inactive beds. Responses included `Cache-Control: no-store`.
- Eleven Chromium checks passed with no response mocks: actual SQL counts/source, historical
  time selection, floor filtering, hover, 3D bed selection, keyboard bed selection, mobile layout,
  a newer SQL snapshot from automatic polling, and no UI error alerts.
- Desktop and 390px mobile screenshots were inspected, with no mobile horizontal overflow.
  Artifacts: `output/playwright/hospital-external-desktop.png` and `hospital-external-mobile.png`.

The preview runs at `http://localhost:5173`, using the Development API at `http://localhost:5026`.
[start-preview-api.ps1](start-preview-api.ps1) reproduces the API process with an explicit
`-TrustDevelopmentCertificate` switch; see [frontend.md](frontend.md) for both startup commands.
The launcher passed PowerShell syntax validation and an actual startup/HTTP smoke check returning
720 SQL beds. Existing application test results above were not rerun for this configuration-only follow-up.
[grant-occupancy-read.sql](grant-occupancy-read.sql) remains an optional setup template, not a script
executed by this verification. Password and table-access blockers are resolved.

An existing cross-origin `sendBeacon` telemetry flush produced a CORS error against the API build
used during browser verification. Ordinary API calls and occupancy rendering succeeded; separate
telemetry changes were in progress in the shared workspace and are not claimed as this feature.

This slice does not implement staff movement, admission movement history, SSE/Redis delivery,
cursor recovery or simulation controls from the broader roadmap.
