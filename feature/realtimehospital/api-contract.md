# Hospital occupancy view contract

Implemented scope: the requested read-only 3D, point-in-time hospital view. The movement,
history, SSE/replay and generator contracts in `frontend.md` and `backend.md` remain future
work; this document does not claim to implement BE-01 for those commands.

## GET /api/hospital-occupancy

Any authenticated application user can read physical occupancy. No patient identity,
admission ID, clinical data or login information is returned. This matches the existing
application-wide read permission; hospital-specific authorization is not yet modeled.

Optional `at` is an ISO 8601 timestamp with UTC offset, URL-encoded. Omit it for server time.
Future or invalid times return 400. Results use UTC timestamps and `Cache-Control: no-store`.
The browser renders local times with its timezone shown in the footer.

```json
{
  "asOf": "2026-09-12T06:00:00+00:00",
  "capturedAt": "2026-09-13T02:00:00+00:00",
  "source": "sql",
  "beds": [{
    "id": "31000000-0000-0000-0000-000000000001",
    "code": "ED-01",
    "number": 1,
    "hospitalId": "05000000-0000-0000-0000-000000000001",
    "hospitalName": "Example hospital",
    "floorId": "05100000-0000-0000-0000-000000000001",
    "floorName": "Floor 1",
    "floorNumber": 1,
    "wardId": "30000000-0000-0000-0000-000000000001",
    "wardName": "Emergency Department",
    "roomId": "05200000-0000-0000-0000-000000000001",
    "roomName": "Room 1",
    "roomNumber": 1,
    "status": "occupied"
  }]
}
```

The example illustrates the wire shape; names and IDs come from SQL in actual responses.
The generated OpenAPI schema is available at `/openapi/v1.json` in Development.

Each bed includes its full physical hierarchy. Only beds attached to the complete hierarchy
are returned; no ward-string admission is assigned an invented bed. Empty inventory returns
`beds: []`. The frontend groups actual IDs rather than generating a layout for SQL results.

Status is one of `occupied`, `available`, `blocked`, `inactive`. At time T:

1. A not-yet-available or retired bed is inactive.
2. Otherwise a bed stay with `started_at <= T < ended_at` is occupied; null end is open.
3. Otherwise a block with `starts_at <= T < ends_at` is blocked; null end is open.
4. Otherwise the bed is available.

All rows are read in one serializable read-only transaction. Percent occupied uses physical
active inventory (`total - inactive`), not staffed capacity. Available does not promise staffing
or clinical suitability. Historical occupancy uses historical intervals with today's hierarchy;
the schema does not version floor/room assignments.

Errors are RFC 9457 problem details with the existing `X-Correlation-Id` response header.
Unauthenticated requests return 401; unreadable/missing database configuration returns 503
with title `Hospital data unavailable`. SQL exception text and credentials are never returned.

## Refresh and sample mode

Current SQL snapshots refresh every 15 seconds after each completed request. Historical mode
does not poll. Requests and timers cancel on mode/time changes, navigation and logout. A failed
refresh retains the last complete snapshot, visibly marked stale with its original timestamp.
This endpoint has no stream cursor and the UI does not claim to have a live subscription.

`src/lib/hospital.ts` provides an explicitly selected synthetic preview (`source: "sample"`):
5 floors × 4 wards × 6 rooms × 6 beds = 720 beds. It includes occupied, available, blocked
and inactive examples. Sample and SQL snapshots are never merged. Sample occupancy changes
deterministically by selected hour and is not a replay of the database's history.

## Running

Run the API and Vite using the existing commands. The hospital overview opens after sign-in.
In Development the API reads the existing `SQL_SERVE_CONNECTION_STRING` key from
`frontend/.env`, including the legacy `ConnectionString=` wrapper if present. No `.env` changes
are required by this feature. Restart the API after changing environment-variable configuration;
the local development file is read on each request.

For hosted environments, use `ConnectionStrings__Hospital` or `SQL_SERVE_CONNECTION_STRING`
on the API process. Only `VITE_API_URL` is a browser setting. Microsoft.Data.SqlClient 6.1.7 is
the sole new backend package; React remains the only frontend runtime dependency.

The database user needs `SELECT` on `dbo.hospitals`, `dbo.floors`, `dbo.wards`, `dbo.rooms`,
`dbo.beds`, `dbo.bed_stays` and `dbo.bed_blocks`. Successful authentication alone does not imply
these permissions. A database administrator can adapt [grant-occupancy-read.sql](grant-occupancy-read.sql)
to grant the required table reads; this endpoint needs no database write permission.

### Local development certificate trust

The configured SQL server's certificate was not trusted by Microsoft.Data.SqlClient during
verification. The separate development preview used `Encrypt=True;TrustServerCertificate=True`
in a process-only connection override. This preserves encryption while bypassing certificate
validation; it must not become an implicit production fallback. Normally, provision a server
certificate trusted by the API host. See [Microsoft's certificate-trust guidance](https://learn.microsoft.com/en-us/troubleshoot/sql/database-engine/connect/error-message-when-you-connect).

After building the API, run `./feature/realtimehospital/start-preview-api.ps1 -TrustDevelopmentCertificate`
from the repository root to opt into that temporary development setting on port 5026. Omitting
the switch retains the configured TLS settings. The script does not edit `.env` or print credentials.
For the UI, set `VITE_API_URL=http://localhost:5026` in the Vite process and use port 5173.
