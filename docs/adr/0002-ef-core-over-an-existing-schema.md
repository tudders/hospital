# 2. EF Core maps an existing schema; the SQL migrations own it

Date: 2026-09-13

## Status

Accepted.

## Context

Patients and Admissions ran on in-memory repositories. The hospital database already exists, with a
schema owned by the tracked SQL migrations in `backend/database`, seeded with a synthetic hospital,
and already read by the occupancy view. The Patients & Admissions page needed to read and write that
database rather than a dictionary that empties on restart.

Two things about the schema shape the decision:

- `dbo.admissions` has no ward column. Where a patient is, is the bed they occupy, recorded in
  `bed_stays`. The aggregate's `Ward` is therefore derived, not stored.
- Uniqueness and capacity rules are already expressed in the database: `ux_patients_mrn`,
  `ux_admissions_one_open_per_patient`, `ward_capacity_periods`, `bed_blocks`.

## Decision

**EF Core maps the existing schema. It never creates or changes it.** There are no EF migrations;
`database/*.sql` stays the single source of truth for the schema, and the `DbContext`s name every
table and column explicitly. Adding a column means writing a migration, not annotating a class.

**Persistence types are separate from aggregates.** `PatientRow` and the hospital rows carry the
columns; `Patient` and `Admission` keep their private constructors, their validation and their
absent setters. Repositories map between them, and a `Rehydrate` factory rebuilds an aggregate from
storage without re-running invariants that were checked on the way in.

**Admitting allocates a bed.** `TryAddActiveAsync` opens a transaction, inserts the admission, and
claims a bed in the requested ward with a single `INSERT ... SELECT TOP (1) ... WITH (UPDLOCK,
ROWLOCK, READPAST)` that also checks the ward's staffed limit. Choosing a bed and claiming it cannot
be separated, so two concurrent admissions cannot take the same one. Discharging closes the stay in
the same transaction that closes the admission, so the bed is allocatable again immediately.

**The write decides, not a prior read.** `AdmitResult` reports what the store found - already
admitted, unknown ward, no bed available - because each of those is a question only the write can
answer without a race. A duplicate MRN is a unique-index violation mapped to a 409, not a
read-then-insert.

**The store is selected by configuration.** With a hospital connection string the domains use EF
Core; without one they use their in-memory repositories. The API starts either way, and the
integration tests stay hermetic without a database or a test container.

**One resolver for the connection string.** `HospitalConnection.Resolve` is used by both EF-backed
domains and the occupancy reader, so they cannot end up pointed at different databases. In
Development only, and only when the connection string says nothing about TLS itself, it trusts the
server certificate - a local SQL Server's self-signed certificate otherwise fails during login.

## Consequences

- The ward on an admission is the ward of its bed. An admission whose bed request is not yet
  fulfilled reads as "Awaiting bed" and is left out of the admissions list, because
  Admitted/Discharged cannot describe it.
- Ward text is resolved against a ward's code, name or type, so `F01-W03`, `Intensive Care Unit` and
  `ICU` all reach the same place. Text that matches nothing is a 400, not a silently created ward.
- A ward can refuse an admission. Free beds and staffed capacity are different limits and the
  tighter one binds, so a half-empty ward with no one rostered correctly reads as full.
- The two contexts share a database, so Admissions' read model of patients is a projection of
  `dbo.patients` rather than a copy. `PatientRegistered` still marks the seam - Admissions never
  calls Patients - but the handler has nothing left to carry across it.
- Cleaning blocks on a released bed, and flow events for the audit timeline, are not written yet.
  Both are in `database.md`; neither is needed to admit and discharge.
