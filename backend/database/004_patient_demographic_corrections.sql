-- Alcidion hospital database
-- Migration: 004_patient_demographic_corrections
-- Dialect: Microsoft SQL Server 2016+
-- Requires: 001_initial_hospital_schema.sql
--
-- Patient rows were write-once: registered and read, never corrected. An MRN
-- typed wrong or a legal name change had no path through the API, so the only
-- remedy was a hand-written UPDATE against production.
--
-- Correcting a patient is a lost-update problem the moment two people can do it,
-- and demographics are exactly what two people correct at once - a clerk fixing
-- the MRN while a nurse fixes the spelling of a name. So the column that makes a
-- correction conditional goes in first, the same way dbo.admissions already
-- carries one: the write matches on it, and the second arrival is refused rather
-- than silently overwriting the first.
--
-- Existing rows start at 0, which is what a freshly registered patient reads as.
-- GO statements are batch separators for SSMS/sqlcmd.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.patients') AND name = N'concurrency_version'
)
BEGIN
    ALTER TABLE dbo.patients
        ADD concurrency_version bigint NOT NULL
            CONSTRAINT df_patients_concurrency_version DEFAULT (0);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.patients') AND name = N'ck_patients_concurrency_version'
)
BEGIN
    ALTER TABLE dbo.patients
        ADD CONSTRAINT ck_patients_concurrency_version CHECK (concurrency_version >= 0);
END;
GO

COMMIT TRANSACTION;
