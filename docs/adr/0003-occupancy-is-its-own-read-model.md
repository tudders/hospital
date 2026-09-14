# 3. The occupancy snapshot is its own read model, and its query stays SQL

Date: 2026-09-14

## Status

Accepted. Extends [0002](0002-ef-core-over-an-existing-schema.md).

## Context

ADR 0002 put Patients and Admissions on EF Core over the existing schema. The hospital occupancy
view was left behind: it lived in `Alcidion.Api`, opened its own `SqlConnection`, and read sixteen
columns out of a `SqlDataReader` by ordinal. It was the only code path in the solution that talked
to the database without EF, and the only one with no test at all.

Two things about the query shape the decision:

- **It belongs to no writing context.** It crosses `hospitals`, `floors`, `wards`, `rooms`, `beds`,
  `bed_stays`, `bed_blocks`, `admissions` and `patients`. Admissions owns some of that and treats
  patients as read-only; nobody owns the physical hierarchy, which arrived with migration 003 and
  was modelled nowhere.
- **Its placement lookup is an `OUTER APPLY (SELECT TOP (1) ...)`,** which seeks
  `ix_bed_stays_bed_history` once per bed.

## Decision

**Occupancy is a read model in its own project.** `Alcidion.Hospital` holds `HospitalSnapshot`,
`IHospitalOccupancyReader` and an `OccupancyDbContext`, registered by the same resolved connection
string as every other context. `Alcidion.Api` keeps the controller and `HospitalConnection.Resolve`
and nothing else. Folding the tables into `AdmissionsDbContext` would have been less work and would
have made Admissions the owner of the whole building.

**The context maps one keyless result shape, not nine tables.** There is nothing to write, nothing
to track, and no second query that would want the tables separately.

**The query stays SQL, run through `FromSql`.** It was translated to LINQ first, and the generated
SQL measured against the original on the seeded hospital (720 beds, 486 stays):

| | server CPU | plan for the placement |
|---|---|---|
| `OUTER APPLY (SELECT TOP (1) ...)` | 10 ms | nested loops, index seek per bed |
| EF Core from LINQ | 41 ms | `ROW_NUMBER` over all open stays, hash join to all admissions, lazy spool |

EF Core rewrites every correlated `FirstOrDefault`/`Take(1)` into a `ROW_NUMBER` window; no LINQ
formulation avoids it, and no index rescues it, because
`started_at <= @at AND (ended_at IS NULL OR ended_at > @at)` cannot seek. The apply's cost grows
with the number of beds. The rewrite's grows with stay history, which only ever increases.

**There is no in-memory occupancy view.** Unlike the writing domains, a snapshot is a read of real
beds or it is nothing, so with no connection string the endpoint reports `hospital_unavailable`. It
still rejects a future timestamp as a 400 first, which is a fact about the request, not the store.

**The SQL-backed tests build their own database.** `Alcidion.Sql.Tests` creates a throwaway database
from `database/*.sql`, runs against it, and drops it; without a server whose login can create
databases, the tests skip rather than fail. In-memory and SQLite providers were not an option: these
tests exist to exercise table hints, triggers, partial unique indexes and `datetimeoffset`, which is
exactly what a substitute provider does not have.

## Consequences

- The API no longer opens a connection anywhere. `Microsoft.Data.SqlClient` stays referenced in
  `Alcidion.Api` only for `SqlConnectionStringBuilder` in `HospitalConnection`, and in the domains
  only for `SqlException.Number`.
- Two queries are deliberately not LINQ, for different reasons, each recorded at the call site:
  bed allocation because LINQ cannot express `WITH (UPDLOCK, ROWLOCK, READPAST)` without reopening
  the race ADR 0002 closed, and the occupancy snapshot because of the plan above. Everything else
  the repositories do is LINQ or `ExecuteUpdate`.
- Sixteen positional `reader.GetGuid(n)` calls became sixteen mapped column names. That trades one
  silent failure for another - a shifted ordinal for a misspelled column - so
  `OccupancyReaderTests` reads the same query positionally and requires the two to agree.
- Transfer was broken before it was tested. `trg_bed_stays_no_overlap` rejects two open stays for
  one admission, and `TransferAsync` allocated the new bed before closing the old stay, so every
  transfer threw. The old stay now closes first, which also stops a same-ward transfer counting the
  patient against that ward's own capacity.
