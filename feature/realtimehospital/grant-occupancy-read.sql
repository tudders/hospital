-- Run as a database administrator in the hospital database.
-- Set the existing database user below (not necessarily the server login name).
-- This script grants only the seven table reads needed by GET /api/hospital-occupancy.
-- It does not create users, change passwords, or grant write access.
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @HospitalReaderUser sysname = N'REPLACE_WITH_DATABASE_USER';

IF @HospitalReaderUser = N'REPLACE_WITH_DATABASE_USER'
    THROW 50001, 'Set HospitalReaderUser to the existing application database user before running.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE name = @HospitalReaderUser AND type IN ('S', 'U', 'E')
)
    THROW 50002, 'The specified database user does not exist in the current database.', 1;

DECLARE @QuotedHospitalReader nvarchar(258) = QUOTENAME(@HospitalReaderUser);
DECLARE @HospitalReadGrants nvarchar(max) =
    N'GRANT SELECT ON OBJECT::dbo.hospitals TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.floors TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.wards TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.rooms TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.beds TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.bed_stays TO ' + @QuotedHospitalReader + N';' +
    N'GRANT SELECT ON OBJECT::dbo.bed_blocks TO ' + @QuotedHospitalReader + N';';

BEGIN TRANSACTION;
EXEC sys.sp_executesql @HospitalReadGrants;
COMMIT TRANSACTION;

-- Reconnect as the application user and retry the occupancy endpoint to verify
-- effective access. An existing DENY can still override a GRANT and needs DBA review.
