-- Alcidion hospital database
-- Migration: 003_expand_full_hospital_mock_data
-- Dialect: Microsoft SQL Server 2016+
-- Requires: 001_initial_hospital_schema.sql and 002_seed_mock_data.sql
--
-- Adds a physical hospital hierarchy and staff assignments, then expands the
-- small flow fixture into one synthetic hospital:
--   1 hospital, 5 floors, 20 wards, 120 rooms, 720 beds, 500 patients.
-- At the snapshot time there are 480 active admissions in beds, 19 waiting
-- admissions, and one discharged patient retained for history.
--
-- Staff ratios for the 480-bed census are intentionally explicit and easy to
-- change: 1 nurse per 4 active patients, 1 care assistant per 8, one physician
-- per 12, plus one ward clerk and one allied-health clinician per ward.

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

DECLARE @asOf datetimeoffset(7) = '2026-09-12 06:00:00 +00:00';

DECLARE @hospital uniqueidentifier = '05000000-0000-0000-0000-000000000001';
DECLARE @floor1 uniqueidentifier = '05100000-0000-0000-0000-000000000001';
DECLARE @floor2 uniqueidentifier = '05100000-0000-0000-0000-000000000002';
DECLARE @floor3 uniqueidentifier = '05100000-0000-0000-0000-000000000003';
DECLARE @floor4 uniqueidentifier = '05100000-0000-0000-0000-000000000004';
DECLARE @floor5 uniqueidentifier = '05100000-0000-0000-0000-000000000005';

DECLARE @pAda uniqueidentifier = '10000000-0000-0000-0000-000000000001';
DECLARE @pGrace uniqueidentifier = '10000000-0000-0000-0000-000000000002';
DECLARE @pAlan uniqueidentifier = '10000000-0000-0000-0000-000000000003';
DECLARE @wEd uniqueidentifier = '30000000-0000-0000-0000-000000000001';
DECLARE @wGeneral uniqueidentifier = '30000000-0000-0000-0000-000000000002';
DECLARE @wIcu uniqueidentifier = '30000000-0000-0000-0000-000000000003';
DECLARE @bedIcu01 uniqueidentifier = '31000000-0000-0000-0000-000000000004';
DECLARE @ttAssessment uniqueidentifier = '40000000-0000-0000-0000-000000000001';

DECLARE @admissionAda uniqueidentifier = '50000000-0000-0000-0000-000000000001';
DECLARE @fullScenario uniqueidentifier = '84000000-0000-0000-0000-000000000001';
DECLARE @fullScenarioVersion uniqueidentifier = '85000000-0000-0000-0000-000000000001';
DECLARE @fullSnapshot uniqueidentifier = '86000000-0000-0000-0000-000000000001';
DECLARE @fullRun uniqueidentifier = '87000000-0000-0000-0000-000000000001';

-- Physical hierarchy --------------------------------------------------------

IF OBJECT_ID(N'dbo.hospitals', N'U') IS NULL
BEGIN
CREATE TABLE dbo.hospitals (
    id          uniqueidentifier NOT NULL,
    code        nvarchar(100) NOT NULL,
    name        nvarchar(200) NOT NULL,
    CONSTRAINT pk_hospitals PRIMARY KEY CLUSTERED (id),
    CONSTRAINT uq_hospitals_code UNIQUE (code),
    CONSTRAINT ck_hospitals_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N'')
);
END;

IF OBJECT_ID(N'dbo.floors', N'U') IS NULL
BEGIN
CREATE TABLE dbo.floors (
    id           uniqueidentifier NOT NULL,
    hospital_id  uniqueidentifier NOT NULL,
    floor_number int NOT NULL,
    code         nvarchar(100) NOT NULL,
    name         nvarchar(200) NOT NULL,
    CONSTRAINT pk_floors PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_floors_hospital FOREIGN KEY (hospital_id) REFERENCES dbo.hospitals (id),
    CONSTRAINT uq_floors_hospital_number UNIQUE (hospital_id, floor_number),
    CONSTRAINT uq_floors_code UNIQUE (code),
    CONSTRAINT ck_floors_number CHECK (floor_number > 0),
    CONSTRAINT ck_floors_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N'')
);
END;

IF COL_LENGTH(N'dbo.wards', N'floor_id') IS NULL
    ALTER TABLE dbo.wards ADD floor_id uniqueidentifier NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_wards_floor')
    ALTER TABLE dbo.wards ADD CONSTRAINT fk_wards_floor FOREIGN KEY (floor_id) REFERENCES dbo.floors (id);

IF OBJECT_ID(N'dbo.rooms', N'U') IS NULL
BEGIN
CREATE TABLE dbo.rooms (
    id           uniqueidentifier NOT NULL,
    floor_id     uniqueidentifier NOT NULL,
    ward_id      uniqueidentifier NOT NULL,
    room_number  int NOT NULL,
    code         nvarchar(100) NOT NULL,
    name         nvarchar(200) NOT NULL,
    CONSTRAINT pk_rooms PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_rooms_floor FOREIGN KEY (floor_id) REFERENCES dbo.floors (id),
    CONSTRAINT fk_rooms_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT uq_rooms_ward_number UNIQUE (ward_id, room_number),
    CONSTRAINT uq_rooms_code UNIQUE (code),
    CONSTRAINT ck_rooms_number CHECK (room_number BETWEEN 1 AND 6),
    CONSTRAINT ck_rooms_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N'')
);
END;

IF COL_LENGTH(N'dbo.beds', N'room_id') IS NULL
    ALTER TABLE dbo.beds ADD room_id uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.beds', N'bed_number') IS NULL
    ALTER TABLE dbo.beds ADD bed_number int NULL;

GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_beds_room')
    ALTER TABLE dbo.beds ADD CONSTRAINT fk_beds_room FOREIGN KEY (room_id) REFERENCES dbo.rooms (id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.wards') AND name = N'ix_wards_floor')
    CREATE INDEX ix_wards_floor ON dbo.wards (floor_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rooms') AND name = N'ix_rooms_ward')
    CREATE INDEX ix_rooms_ward ON dbo.rooms (ward_id, room_number);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.beds') AND name = N'ux_beds_room_number')
    CREATE UNIQUE INDEX ux_beds_room_number ON dbo.beds (room_id, bed_number) WHERE room_id IS NOT NULL AND bed_number IS NOT NULL;

-- Staffing and patient-level care allocation -------------------------------

IF OBJECT_ID(N'dbo.staff_members', N'U') IS NULL
BEGIN
CREATE TABLE dbo.staff_members (
    id                 uniqueidentifier NOT NULL,
    hospital_id        uniqueidentifier NOT NULL,
    employee_number    nvarchar(100) NOT NULL,
    given_name         nvarchar(200) NOT NULL,
    family_name        nvarchar(200) NOT NULL,
    role               nvarchar(100) NOT NULL,
    specialty          nvarchar(200) NULL,
    employment_status  nvarchar(50) NOT NULL CONSTRAINT df_staff_members_status DEFAULT (N'active'),
    CONSTRAINT pk_staff_members PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_staff_members_hospital FOREIGN KEY (hospital_id) REFERENCES dbo.hospitals (id),
    CONSTRAINT uq_staff_members_employee_number UNIQUE (employee_number),
    CONSTRAINT ck_staff_members_text CHECK (
        LTRIM(RTRIM(employee_number)) <> N''
        AND LTRIM(RTRIM(given_name)) <> N''
        AND LTRIM(RTRIM(family_name)) <> N''
        AND LTRIM(RTRIM(role)) <> N''
    )
);
END;

IF OBJECT_ID(N'dbo.staff_ward_assignments', N'U') IS NULL
BEGIN
CREATE TABLE dbo.staff_ward_assignments (
    id          uniqueidentifier NOT NULL,
    staff_id    uniqueidentifier NOT NULL,
    ward_id     uniqueidentifier NOT NULL,
    shift_code  nvarchar(50) NOT NULL,
    starts_at   datetimeoffset(7) NOT NULL,
    ends_at     datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_staff_ward_assignments PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_staff_ward_assignments_staff FOREIGN KEY (staff_id) REFERENCES dbo.staff_members (id),
    CONSTRAINT fk_staff_ward_assignments_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT ck_staff_ward_assignments_time CHECK (ends_at > starts_at),
    CONSTRAINT ck_staff_ward_assignments_shift CHECK (LTRIM(RTRIM(shift_code)) <> N'')
);
END;

IF OBJECT_ID(N'dbo.patient_care_assignments', N'U') IS NULL
BEGIN
CREATE TABLE dbo.patient_care_assignments (
    id               uniqueidentifier NOT NULL,
    admission_id     uniqueidentifier NOT NULL,
    staff_id         uniqueidentifier NOT NULL,
    assignment_role  nvarchar(100) NOT NULL,
    starts_at        datetimeoffset(7) NOT NULL,
    ends_at          datetimeoffset(7) NULL,
    CONSTRAINT pk_patient_care_assignments PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_patient_care_assignments_admission FOREIGN KEY (admission_id) REFERENCES dbo.admissions (id),
    CONSTRAINT fk_patient_care_assignments_staff FOREIGN KEY (staff_id) REFERENCES dbo.staff_members (id),
    CONSTRAINT ck_patient_care_assignments_time CHECK (ends_at IS NULL OR ends_at > starts_at),
    CONSTRAINT ck_patient_care_assignments_role CHECK (LTRIM(RTRIM(assignment_role)) <> N'')
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.staff_ward_assignments') AND name = N'ix_staff_ward_assignments_ward_time')
    CREATE INDEX ix_staff_ward_assignments_ward_time ON dbo.staff_ward_assignments (ward_id, starts_at, ends_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.patient_care_assignments') AND name = N'ix_patient_care_assignments_admission')
    CREATE INDEX ix_patient_care_assignments_admission ON dbo.patient_care_assignments (admission_id, starts_at);

GO

-- Variables are batch-scoped in sqlcmd/SSMS; redeclare them after the DDL batch.
DECLARE @asOf datetimeoffset(7) = '2026-09-12 06:00:00 +00:00';
DECLARE @hospital uniqueidentifier = '05000000-0000-0000-0000-000000000001';
DECLARE @floor1 uniqueidentifier = '05100000-0000-0000-0000-000000000001';
DECLARE @floor2 uniqueidentifier = '05100000-0000-0000-0000-000000000002';
DECLARE @floor3 uniqueidentifier = '05100000-0000-0000-0000-000000000003';
DECLARE @floor4 uniqueidentifier = '05100000-0000-0000-0000-000000000004';
DECLARE @floor5 uniqueidentifier = '05100000-0000-0000-0000-000000000005';
DECLARE @pAda uniqueidentifier = '10000000-0000-0000-0000-000000000001';
DECLARE @pGrace uniqueidentifier = '10000000-0000-0000-0000-000000000002';
DECLARE @pAlan uniqueidentifier = '10000000-0000-0000-0000-000000000003';
DECLARE @wEd uniqueidentifier = '30000000-0000-0000-0000-000000000001';
DECLARE @wGeneral uniqueidentifier = '30000000-0000-0000-0000-000000000002';
DECLARE @wIcu uniqueidentifier = '30000000-0000-0000-0000-000000000003';
DECLARE @bedIcu01 uniqueidentifier = '31000000-0000-0000-0000-000000000004';
DECLARE @ttAssessment uniqueidentifier = '40000000-0000-0000-0000-000000000001';
DECLARE @admissionAda uniqueidentifier = '50000000-0000-0000-0000-000000000001';
DECLARE @fullScenario uniqueidentifier = '84000000-0000-0000-0000-000000000001';
DECLARE @fullScenarioVersion uniqueidentifier = '85000000-0000-0000-0000-000000000001';
DECLARE @fullSnapshot uniqueidentifier = '86000000-0000-0000-0000-000000000001';
DECLARE @fullRun uniqueidentifier = '87000000-0000-0000-0000-000000000001';

INSERT INTO dbo.hospitals (id, code, name)
SELECT @hospital, N'ALC-HOSP-01', N'Alcidion Synthetic University Hospital'
WHERE NOT EXISTS (SELECT 1 FROM dbo.hospitals WHERE id = @hospital);

INSERT INTO dbo.floors (id, hospital_id, floor_number, code, name)
SELECT v.id, @hospital, v.floor_number, CONCAT(N'F', RIGHT(N'0' + CONVERT(nvarchar(2), v.floor_number), 2)),
       CONCAT(N'Floor ', v.floor_number)
FROM (VALUES
    (@floor1, 1), (@floor2, 2), (@floor3, 3), (@floor4, 4), (@floor5, 5)
) v(id, floor_number)
WHERE NOT EXISTS (SELECT 1 FROM dbo.floors f WHERE f.id = v.id);

-- Re-home the three original fixture wards as the first three wards.
UPDATE w
SET floor_id = CASE w.id WHEN @wEd THEN @floor1 WHEN @wGeneral THEN @floor1 WHEN @wIcu THEN @floor1 END,
    code = CASE w.id WHEN @wEd THEN N'F01-W01' WHEN @wGeneral THEN N'F01-W02' WHEN @wIcu THEN N'F01-W03' END,
    name = CASE w.id WHEN @wEd THEN N'Emergency Department'
                    WHEN @wGeneral THEN N'General Medicine'
                    WHEN @wIcu THEN N'Intensive Care Unit' END,
    ward_type = CASE w.id WHEN @wEd THEN N'emergency' WHEN @wGeneral THEN N'general' WHEN @wIcu THEN N'icu' END
FROM dbo.wards w
WHERE w.id IN (@wEd, @wGeneral, @wIcu);

;WITH WardNumbers AS (
    SELECT CONVERT(int, 4) AS ward_number
    UNION ALL
    SELECT ward_number + 1 FROM WardNumbers WHERE ward_number < 20
)
INSERT INTO dbo.wards (id, floor_id, code, name, ward_type)
SELECT CONVERT(uniqueidentifier, CONCAT('30000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), n.ward_number), 12))),
       f.id,
       CONCAT(N'F', RIGHT(N'0' + CONVERT(nvarchar(2), ((n.ward_number - 1) / 4) + 1), 2), N'-W', RIGHT(N'0' + CONVERT(nvarchar(2), ((n.ward_number - 1) % 4) + 1), 2)),
       CONCAT(N'Ward ', n.ward_number, N' - ', CASE ((n.ward_number - 1) % 4) + 1
           WHEN 1 THEN N'General Medicine' WHEN 2 THEN N'Surgical Services'
           WHEN 3 THEN N'Women''s Health' ELSE N'Rehabilitation' END),
       CASE ((n.ward_number - 1) % 4) + 1
           WHEN 1 THEN N'general' WHEN 2 THEN N'surgical' WHEN 3 THEN N'maternity' ELSE N'rehabilitation' END
FROM WardNumbers n
JOIN dbo.floors f ON f.floor_number = ((n.ward_number - 1) / 4) + 1
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.wards w
    WHERE w.id = CONVERT(uniqueidentifier, CONCAT('30000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), n.ward_number), 12)))
)
OPTION (MAXRECURSION 100);

;WITH WardMap AS (
    SELECT w.id AS ward_id, f.id AS floor_id, f.floor_number,
           CONVERT(int, RIGHT(w.code, 2)) AS ward_number
    FROM dbo.wards w
    JOIN dbo.floors f ON f.id = w.floor_id
), RoomNumbers AS (
    SELECT CONVERT(int, 1) AS room_number
    UNION ALL
    SELECT room_number + 1 FROM RoomNumbers WHERE room_number < 6
)
INSERT INTO dbo.rooms (id, floor_id, ward_id, room_number, code, name)
SELECT CONVERT(uniqueidentifier, CONCAT('06000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), ((wm.floor_number - 1) * 24) + ((wm.ward_number - 1) * 6) + rn.room_number), 12))),
       wm.floor_id,
       wm.ward_id,
       rn.room_number,
       CONCAT(N'F', RIGHT(N'0' + CONVERT(nvarchar(2), wm.floor_number), 2), N'-W', RIGHT(N'0' + CONVERT(nvarchar(2), wm.ward_number), 2), N'-R', RIGHT(N'0' + CONVERT(nvarchar(2), rn.room_number), 2)),
       CONCAT(N'Room ', rn.room_number)
FROM WardMap wm
CROSS JOIN RoomNumbers rn
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.rooms r
    WHERE r.ward_id = wm.ward_id AND r.room_number = rn.room_number
)
OPTION (MAXRECURSION 100);

-- Put the original four beds into the hierarchy before filling every room.
UPDATE b SET room_id = r.id, bed_number = 1, code = N'F01-W01-R01-B01', ward_id = @wEd, bed_type = N'assessment'
FROM dbo.beds b JOIN dbo.rooms r ON r.code = N'F01-W01-R01' WHERE b.id = '31000000-0000-0000-0000-000000000001';
UPDATE b SET room_id = r.id, bed_number = 1, code = N'F01-W02-R01-B01', ward_id = @wGeneral, bed_type = N'general'
FROM dbo.beds b JOIN dbo.rooms r ON r.code = N'F01-W02-R01' WHERE b.id = '31000000-0000-0000-0000-000000000002';
UPDATE b SET room_id = r.id, bed_number = 2, code = N'F01-W02-R01-B02', ward_id = @wGeneral, bed_type = N'general'
FROM dbo.beds b JOIN dbo.rooms r ON r.code = N'F01-W02-R01' WHERE b.id = '31000000-0000-0000-0000-000000000003';
UPDATE b SET room_id = r.id, bed_number = 1, code = N'F01-W03-R01-B01', ward_id = @wIcu, bed_type = N'icu'
FROM dbo.beds b JOIN dbo.rooms r ON r.code = N'F01-W03-R01' WHERE b.id = @bedIcu01;

;WITH WardMap AS (
    SELECT w.id AS ward_id, f.floor_number, CONVERT(int, RIGHT(w.code, 2)) AS ward_number, w.ward_type
    FROM dbo.wards w JOIN dbo.floors f ON f.id = w.floor_id
), BedNumbers AS (
    SELECT CONVERT(int, 1) AS bed_number
    UNION ALL
    SELECT bed_number + 1 FROM BedNumbers WHERE bed_number < 6
)
INSERT INTO dbo.beds (id, room_id, ward_id, bed_number, code, bed_type, available_from)
SELECT CONVERT(uniqueidentifier, CONCAT('31100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), ((wm.floor_number - 1) * 144) + ((wm.ward_number - 1) * 36) + ((r.room_number - 1) * 6) + bn.bed_number), 12))),
       r.id,
       wm.ward_id,
       bn.bed_number,
       CONCAT(r.code, N'-B', RIGHT(N'0' + CONVERT(nvarchar(2), bn.bed_number), 2)),
       CASE wm.ward_type WHEN N'icu' THEN N'icu' WHEN N'emergency' THEN N'assessment' ELSE N'general' END,
       '2026-01-01 00:00:00 +00:00'
FROM WardMap wm
JOIN dbo.rooms r ON r.ward_id = wm.ward_id
CROSS JOIN BedNumbers bn
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.beds b WHERE b.room_id = r.id AND b.bed_number = bn.bed_number
)
OPTION (MAXRECURSION 100);

-- Every ward has 36 physical beds and normally staffs 30 of them.
UPDATE cp
SET staffed_bed_limit = 30
FROM dbo.ward_capacity_periods cp
JOIN dbo.wards w ON w.id = cp.ward_id
WHERE w.floor_id IS NOT NULL;

INSERT INTO dbo.ward_capacity_periods (id, ward_id, starts_at, staffed_bed_limit)
SELECT CONVERT(uniqueidentifier, CONCAT('32100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), ROW_NUMBER() OVER (ORDER BY w.code)), 12))),
       w.id, '2026-01-01 00:00:00 +00:00', 30
FROM dbo.wards w
WHERE w.floor_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ward_capacity_periods cp
      WHERE cp.ward_id = w.id AND cp.starts_at = '2026-01-01 00:00:00 +00:00'
  );

-- Make the existing treatment catalogue usable throughout the hospital.
INSERT INTO dbo.ward_treatment_capabilities (id, ward_id, treatment_type_id, starts_at, max_concurrent)
SELECT CONVERT(uniqueidentifier, CONCAT('41100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), ROW_NUMBER() OVER (ORDER BY w.code, t.code)), 12))),
       w.id, t.id, '2026-01-01 00:00:00 +00:00',
       CASE t.code WHEN N'ASSESS' THEN 6 WHEN N'OXYGEN' THEN 6 ELSE 12 END
FROM dbo.wards w
CROSS JOIN dbo.treatment_types t
WHERE w.floor_id IS NOT NULL
  AND (t.code = N'ASSESS' OR (t.code = N'OXYGEN' AND w.ward_type = N'icu') OR (t.code = N'PHYSIO' AND w.ward_type <> N'icu'))
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ward_treatment_capabilities c
      WHERE c.ward_id = w.id AND c.treatment_type_id = t.id
        AND c.starts_at = '2026-01-01 00:00:00 +00:00'
  );

-- Patients, conditions and weights ------------------------------------------

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.patients (id, mrn, given_name, family_name, date_of_birth, gender, registered_at)
SELECT CONVERT(uniqueidentifier, CONCAT('10000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONCAT(N'ALC-', RIGHT(N'0000' + CONVERT(nvarchar(4), patient_number), 4)),
       CHOOSE(((patient_number - 1) % 12) + 1, N'Avery', N'Casey', N'Jordan', N'Morgan', N'Riley', N'Taylor', N'Quinn', N'Harper', N'Parker', N'Reese', N'Rowan', N'Sage'),
       CONCAT(N'Synthetic-', RIGHT(N'0000' + CONVERT(nvarchar(4), patient_number), 4)),
       DATEADD(DAY, -((patient_number * 19) % 25000), CONVERT(date, '2026-01-01')),
       CASE patient_number % 3 WHEN 0 THEN N'female' WHEN 1 THEN N'male' ELSE N'unknown' END,
       DATEADD(MINUTE, -patient_number * 13, @asOf)
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.patients p
    WHERE p.id = CONVERT(uniqueidentifier, CONCAT('10000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.patient_weight_measurements (id, patient_id, weight_kg, measured_at)
SELECT CONVERT(uniqueidentifier, CONCAT('11100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('10000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(decimal(8, 3), 55.000 + ((patient_number * 17) % 420) / 10.0),
       DATEADD(MINUTE, -patient_number * 11, @asOf)
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.patient_weight_measurements m
    WHERE m.id = CONVERT(uniqueidentifier, CONCAT('11100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.patient_conditions (id, patient_id, condition_type_id, onset_at, recorded_at, expected_resolved_at, resolved_at, severity)
SELECT CONVERT(uniqueidentifier, CONCAT('21100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('10000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CASE patient_number % 3 WHEN 0 THEN '20000000-0000-0000-0000-000000000001' WHEN 1 THEN '20000000-0000-0000-0000-000000000002' ELSE '20000000-0000-0000-0000-000000000003' END,
       DATEADD(HOUR, -(patient_number % 168), @asOf),
       DATEADD(MINUTE, 5, DATEADD(HOUR, -(patient_number % 168), @asOf)),
       DATEADD(DAY, 3 + (patient_number % 8), @asOf),
       CASE WHEN patient_number % 5 = 0 THEN DATEADD(DAY, 1 + (patient_number % 3), @asOf) ELSE NULL END,
       CASE patient_number % 4 WHEN 0 THEN N'mild' WHEN 1 THEN N'moderate' WHEN 2 THEN N'severe' ELSE N'low' END
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.patient_conditions c
    WHERE c.id = CONVERT(uniqueidentifier, CONCAT('21100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

-- Staff: 125 nurses, 63 care assistants, 42 physicians, 20 ward clerks,
-- and 20 allied-health clinicians.
;WITH StaffNumbers AS (
    SELECT CONVERT(int, 1) AS staff_number
    UNION ALL
    SELECT staff_number + 1 FROM StaffNumbers WHERE staff_number < 270
)
INSERT INTO dbo.staff_members (id, hospital_id, employee_number, given_name, family_name, role, specialty)
SELECT CONVERT(uniqueidentifier, CONCAT('73000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), staff_number), 12))),
       @hospital,
       CONCAT(N'EMP-', RIGHT(N'0000' + CONVERT(nvarchar(4), staff_number), 4)),
       CHOOSE(((staff_number - 1) % 12) + 1, N'Avery', N'Casey', N'Jordan', N'Morgan', N'Riley', N'Taylor', N'Quinn', N'Harper', N'Parker', N'Reese', N'Rowan', N'Sage'),
       CONCAT(N'Staff-', RIGHT(N'0000' + CONVERT(nvarchar(4), staff_number), 4)),
       CASE WHEN staff_number <= 125 THEN N'registered_nurse'
            WHEN staff_number <= 188 THEN N'care_assistant'
            WHEN staff_number <= 230 THEN N'physician'
            WHEN staff_number <= 250 THEN N'ward_clerk'
            ELSE N'allied_health' END,
       CASE WHEN staff_number <= 125 THEN N'inpatient nursing'
            WHEN staff_number <= 188 THEN N'ward support'
            WHEN staff_number <= 230 THEN N'general medicine'
            WHEN staff_number <= 250 THEN N'ward operations'
            ELSE N'rehabilitation and therapy' END
FROM StaffNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.staff_members s
    WHERE s.id = CONVERT(uniqueidentifier, CONCAT('73000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), staff_number), 12)))
)
OPTION (MAXRECURSION 400);

;WITH StaffNumbers AS (
    SELECT CONVERT(int, 1) AS staff_number
    UNION ALL
    SELECT staff_number + 1 FROM StaffNumbers WHERE staff_number < 270
), WardMap AS (
    SELECT w.id AS ward_id, ROW_NUMBER() OVER (ORDER BY w.code) AS ward_number
    FROM dbo.wards w WHERE w.floor_id IS NOT NULL
)
INSERT INTO dbo.staff_ward_assignments (id, staff_id, ward_id, shift_code, starts_at, ends_at)
SELECT CONVERT(uniqueidentifier, CONCAT('74000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), sn.staff_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('73000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), sn.staff_number), 12))),
       wm.ward_id, CASE WHEN sn.staff_number % 2 = 0 THEN N'day' ELSE N'evening' END,
       DATEADD(HOUR, CASE WHEN sn.staff_number % 2 = 0 THEN 7 ELSE 19 END, CONVERT(datetimeoffset(7), '2026-09-12 00:00:00 +00:00')),
       DATEADD(HOUR, CASE WHEN sn.staff_number % 2 = 0 THEN 19 ELSE 31 END, CONVERT(datetimeoffset(7), '2026-09-12 00:00:00 +00:00'))
FROM StaffNumbers sn
JOIN WardMap wm ON wm.ward_number = ((sn.staff_number - 1) % 20) + 1
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.staff_ward_assignments a
    WHERE a.id = CONVERT(uniqueidentifier, CONCAT('74000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), sn.staff_number), 12)))
)
OPTION (MAXRECURSION 400);

-- Admissions and bed occupancy ---------------------------------------------

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.admissions (id, patient_id, requested_at, admitted_at, expected_discharge_at, priority)
SELECT CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('10000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       DATEADD(HOUR, -(patient_number % 72), @asOf),
       CASE WHEN patient_number <= 482 THEN DATEADD(MINUTE, 30, DATEADD(HOUR, -(patient_number % 72), @asOf)) ELSE NULL END,
       CASE WHEN patient_number <= 482 THEN DATEADD(DAY, 3 + (patient_number % 8), DATEADD(MINUTE, 30, DATEADD(HOUR, -(patient_number % 72), @asOf))) ELSE NULL END,
       1 + (patient_number % 3)
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.admissions a
    WHERE a.id = CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

-- Give Ada a current ICU stay after her original transfer; the original stay
-- remains intact as history.
INSERT INTO dbo.bed_stays (id, admission_id, bed_id, started_at, expected_end_at)
SELECT '71100000-0000-0000-0000-000000000001', @admissionAda, @bedIcu01, @asOf, DATEADD(DAY, 4, @asOf)
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_stays WHERE id = '71100000-0000-0000-0000-000000000001');

;WITH ActivePatients AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM ActivePatients WHERE patient_number < 482
), PatientRows AS (
    SELECT patient_number, ROW_NUMBER() OVER (ORDER BY patient_number) AS row_number FROM ActivePatients
), BedRows AS (
    SELECT b.id AS bed_id, b.ward_id, b.bed_type, ROW_NUMBER() OVER (ORDER BY b.code) AS row_number
    FROM dbo.beds b
    JOIN dbo.rooms r ON r.id = b.room_id
    WHERE b.id <> @bedIcu01
      AND ((r.room_number - 1) * 6) + b.bed_number <= 24
)
INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, required_bed_type, requested_at, priority, fulfilled_at)
SELECT CONVERT(uniqueidentifier, CONCAT('70100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12))),
       b.ward_id, b.bed_type, DATEADD(MINUTE, -30, @asOf), 1 + (p.patient_number % 3), @asOf
FROM PatientRows p JOIN BedRows b ON b.row_number = p.row_number
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.bed_requests r
    WHERE r.id = CONVERT(uniqueidentifier, CONCAT('70100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

;WITH ActivePatients AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM ActivePatients WHERE patient_number < 482
), PatientRows AS (
    SELECT patient_number, ROW_NUMBER() OVER (ORDER BY patient_number) AS row_number FROM ActivePatients
), BedRows AS (
    SELECT b.id AS bed_id, ROW_NUMBER() OVER (ORDER BY b.code) AS row_number
    FROM dbo.beds b
    JOIN dbo.rooms r ON r.id = b.room_id
    WHERE b.id <> @bedIcu01
      AND ((r.room_number - 1) * 6) + b.bed_number <= 24
)
INSERT INTO dbo.bed_stays (id, admission_id, bed_id, started_at, expected_end_at)
SELECT CONVERT(uniqueidentifier, CONCAT('71100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12))),
       b.bed_id, @asOf, DATEADD(DAY, 3 + (p.patient_number % 8), @asOf)
FROM PatientRows p JOIN BedRows b ON b.row_number = p.row_number
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.bed_stays s
    WHERE s.id = CONVERT(uniqueidentifier, CONCAT('71100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), p.patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

-- The remaining 18 generated admissions are waiting for a general bed.
;WITH PatientNumbers AS (
    SELECT CONVERT(int, 483) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, required_bed_type, requested_at, priority)
SELECT CONVERT(uniqueidentifier, CONCAT('70100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       @wGeneral, N'general', DATEADD(HOUR, -(patient_number % 48), @asOf), 2 + (patient_number % 2)
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.bed_requests r
    WHERE r.id = CONVERT(uniqueidentifier, CONCAT('70100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 100);

-- One assessment order per generated patient, with a matching condition.
;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.treatment_orders (id, admission_id, treatment_type_id, ordered_at, ready_at, planned_duration_minutes, priority)
SELECT CONVERT(uniqueidentifier, CONCAT('60100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       @ttAssessment,
       DATEADD(MINUTE, 5, DATEADD(HOUR, -(patient_number % 72), @asOf)),
       DATEADD(MINUTE, 10, DATEADD(HOUR, -(patient_number % 72), @asOf)),
       60, 1 + (patient_number % 3)
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.treatment_orders o
    WHERE o.id = CONVERT(uniqueidentifier, CONCAT('60100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.treatment_order_conditions (treatment_order_id, patient_condition_id)
SELECT CONVERT(uniqueidentifier, CONCAT('60100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('21100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.treatment_order_conditions oc
    WHERE oc.treatment_order_id = CONVERT(uniqueidentifier, CONCAT('60100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

-- Complete assessment sessions for the 479 generated active admissions.
;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 482
)
INSERT INTO dbo.treatment_sessions (id, treatment_order_id, ward_id, started_at, expected_end_at, ended_at, outcome)
SELECT CONVERT(uniqueidentifier, CONCAT('72100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('60100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       b.ward_id,
       DATEADD(MINUTE, 15, DATEADD(HOUR, -(patient_number % 72), @asOf)),
       DATEADD(MINUTE, 75, DATEADD(HOUR, -(patient_number % 72), @asOf)),
       DATEADD(MINUTE, 75, DATEADD(HOUR, -(patient_number % 72), @asOf)), N'completed'
FROM PatientNumbers p
JOIN dbo.bed_stays s ON s.admission_id = CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
JOIN dbo.beds b ON b.id = s.bed_id
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.treatment_sessions ts
    WHERE ts.id = CONVERT(uniqueidentifier, CONCAT('72100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

-- Assign one nurse, one care assistant and one physician to every active
-- admission. The assignments are deliberately patient-linked, not just ward
-- staffing, so downstream dashboards can show a care team per patient.
;WITH ActiveAdmissions AS (
    SELECT @admissionAda AS admission_id
    UNION ALL
    SELECT a.id FROM dbo.admissions a WHERE CONVERT(varchar(36), a.id) LIKE '50100000-0000-0000-0000-%' AND a.admitted_at IS NOT NULL
), NumberedAdmissions AS (
    SELECT admission_id, ROW_NUMBER() OVER (ORDER BY admission_id) AS patient_number FROM ActiveAdmissions
), Roles AS (
    SELECT N'primary_nurse' AS assignment_role, CONVERT(int, 1) AS role_number, CONVERT(int, 1) AS first_staff, CONVERT(int, 125) AS staff_count
    UNION ALL SELECT N'care_assistant', 2, 126, 63
    UNION ALL SELECT N'attending_physician', 3, 189, 42
)
INSERT INTO dbo.patient_care_assignments (id, admission_id, staff_id, assignment_role, starts_at)
SELECT CONVERT(uniqueidentifier, CONCAT('75000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), ((n.patient_number - 1) * 3) + r.role_number), 12))),
       n.admission_id,
       CONVERT(uniqueidentifier, CONCAT('73000000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), r.first_staff + ((n.patient_number - 1) % r.staff_count)), 12))),
       r.assignment_role,
       COALESCE(a.admitted_at, @asOf)
FROM NumberedAdmissions n
JOIN dbo.admissions a ON a.id = n.admission_id
CROSS JOIN Roles r
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.patient_care_assignments pca
    WHERE pca.admission_id = n.admission_id AND pca.assignment_role = r.assignment_role AND pca.ends_at IS NULL
);

-- Flow and simulation records ----------------------------------------------

;WITH PatientNumbers AS (
    SELECT CONVERT(int, 4) AS patient_number
    UNION ALL
    SELECT patient_number + 1 FROM PatientNumbers WHERE patient_number < 500
)
INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT CONVERT(uniqueidentifier, CONCAT('90100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       CONVERT(uniqueidentifier, CONCAT('50100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12))),
       N'requested', DATEADD(HOUR, -(patient_number % 72), @asOf), DATEADD(MINUTE, 1, DATEADD(HOUR, -(patient_number % 72), @asOf)),
       CONCAT(N'full-hospital-', RIGHT(N'0000' + CONVERT(nvarchar(4), patient_number), 4)),
       CONCAT(N'{"source":"seed","patient_number":', patient_number, N'}')
FROM PatientNumbers
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.flow_events e
    WHERE e.id = CONVERT(uniqueidentifier, CONCAT('90100000-0000-0000-0000-', RIGHT('000000000000' + CONVERT(varchar(12), patient_number), 12)))
)
OPTION (MAXRECURSION 1000);

INSERT INTO dbo.simulation_scenarios (id, name, description, created_at)
SELECT @fullScenario, N'Full hospital baseline', N'500 synthetic patients across five floors, twenty wards and 720 beds.', @asOf
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_scenarios WHERE id = @fullScenario);

INSERT INTO dbo.simulation_scenario_versions (id, scenario_id, version, parameters_json, created_at)
SELECT @fullScenarioVersion, @fullScenario, 1,
       N'{"floors":5,"wards_per_floor":4,"rooms_per_ward":6,"beds_per_room":6,"patients":500,"active_admissions":480,"pending_admissions":19,"nurse_to_patient":4,"care_assistant_to_patient":8,"physician_to_patient":12}', @asOf
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_scenario_versions WHERE id = @fullScenarioVersion);

INSERT INTO dbo.simulation_snapshots (id, captured_at, schema_version, state_json)
SELECT @fullSnapshot, @asOf, N'1.1',
       N'{"hospitals":1,"floors":5,"wards":20,"rooms":120,"beds":720,"patients":500,"active_admissions":480,"pending_bed_requests":19,"staff_members":270}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_snapshots WHERE id = @fullSnapshot);

INSERT INTO dbo.simulation_runs (id, scenario_version_id, snapshot_id, random_seed, engine_version, simulation_start_at, simulation_end_at, started_at, finished_at, status)
SELECT @fullRun, @fullScenarioVersion, @fullSnapshot, 2026091201, N'mock-engine-1.1', @asOf, DATEADD(DAY, 1, @asOf), @asOf, DATEADD(MINUTE, 2, @asOf), N'completed'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_runs WHERE id = @fullRun);

INSERT INTO dbo.simulation_events (id, run_id, sequence_no, simulated_at, event_type, entity_type, entity_id, payload_json)
SELECT '88000000-0000-0000-0000-000000000001', @fullRun, 1, @asOf, N'capacity_sampled', N'hospital', @hospital,
       N'{"beds":720,"occupied_beds":480,"staffed_bed_limit":600}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_events WHERE id = '88000000-0000-0000-0000-000000000001');

INSERT INTO dbo.simulation_metric_samples (id, run_id, simulated_at, metric_name, value, unit)
SELECT '89000000-0000-0000-0000-000000000001', @fullRun, @asOf, N'occupancy_percentage', 66.666667, N'percent'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_metric_samples WHERE id = '89000000-0000-0000-0000-000000000001');

COMMIT TRANSACTION;
