-- Alcidion hospital database
-- Migration: 001_initial_hospital_schema
-- Dialect: Microsoft SQL Server 2016+
--
-- Application code supplies uniqueidentifier values and UTC timestamps. The
-- allocation, transfer and discharge workflows must run in transactions and
-- lock the relevant ward, bed and capability rows while checking capacity.
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

CREATE TABLE dbo.patients (
    id              uniqueidentifier NOT NULL,
    mrn             nvarchar(100) NOT NULL,
    given_name      nvarchar(200) NOT NULL,
    family_name     nvarchar(200) NOT NULL,
    date_of_birth   date NOT NULL,
    gender          nvarchar(100) NOT NULL CONSTRAINT df_patients_gender DEFAULT (N'unknown'),
    registered_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_patients PRIMARY KEY CLUSTERED (id),
    CONSTRAINT ck_patients_mrn CHECK (
        mrn <> N''
        AND mrn COLLATE Latin1_General_100_BIN2 = UPPER(LTRIM(RTRIM(mrn))) COLLATE Latin1_General_100_BIN2
    ),
    CONSTRAINT ck_patients_names CHECK (LTRIM(RTRIM(given_name)) <> N'' AND LTRIM(RTRIM(family_name)) <> ''),
    CONSTRAINT ck_patients_gender CHECK (LTRIM(RTRIM(gender)) <> N'')
);

CREATE UNIQUE INDEX ux_patients_mrn ON dbo.patients (mrn);

CREATE TABLE dbo.patient_weight_measurements (
    id            uniqueidentifier NOT NULL,
    patient_id    uniqueidentifier NOT NULL,
    weight_kg     decimal(8, 3) NOT NULL,
    measured_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_patient_weight_measurements PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_patient_weight_measurements_patient FOREIGN KEY (patient_id) REFERENCES dbo.patients (id),
    CONSTRAINT ck_patient_weight_measurements_weight CHECK (weight_kg > 0)
);

CREATE TABLE dbo.condition_types (
    id            uniqueidentifier NOT NULL,
    code          nvarchar(100) NOT NULL,
    name          nvarchar(200) NOT NULL,
    description   nvarchar(1000) NULL,
    CONSTRAINT pk_condition_types PRIMARY KEY CLUSTERED (id),
    CONSTRAINT uq_condition_types_code UNIQUE (code),
    CONSTRAINT ck_condition_types_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N'')
);

CREATE TABLE dbo.patient_conditions (
    id                    uniqueidentifier NOT NULL,
    patient_id            uniqueidentifier NOT NULL,
    condition_type_id     uniqueidentifier NOT NULL,
    onset_at              datetimeoffset(7) NOT NULL,
    recorded_at           datetimeoffset(7) NOT NULL,
    expected_resolved_at  datetimeoffset(7) NULL,
    resolved_at           datetimeoffset(7) NULL,
    severity              nvarchar(100) NOT NULL CONSTRAINT df_patient_conditions_severity DEFAULT (N'unknown'),
    CONSTRAINT pk_patient_conditions PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_patient_conditions_patient FOREIGN KEY (patient_id) REFERENCES dbo.patients (id),
    CONSTRAINT fk_patient_conditions_type FOREIGN KEY (condition_type_id) REFERENCES dbo.condition_types (id),
    CONSTRAINT ck_patient_conditions_times CHECK (
        (expected_resolved_at IS NULL OR expected_resolved_at >= onset_at)
        AND (resolved_at IS NULL OR resolved_at >= onset_at)
        AND recorded_at >= onset_at
    ),
    CONSTRAINT ck_patient_conditions_severity CHECK (LTRIM(RTRIM(severity)) <> N'')
);

CREATE TABLE dbo.wards (
    id          uniqueidentifier NOT NULL,
    code        nvarchar(100) NOT NULL,
    name        nvarchar(200) NOT NULL,
    ward_type   nvarchar(100) NOT NULL,
    CONSTRAINT pk_wards PRIMARY KEY CLUSTERED (id),
    CONSTRAINT uq_wards_code UNIQUE (code),
    CONSTRAINT ck_wards_text CHECK (
        LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N'' AND LTRIM(RTRIM(ward_type)) <> N''
    )
);

CREATE TABLE dbo.beds (
    id              uniqueidentifier NOT NULL,
    ward_id         uniqueidentifier NOT NULL,
    code            nvarchar(100) NOT NULL,
    bed_type        nvarchar(100) NOT NULL,
    available_from  datetimeoffset(7) NOT NULL,
    retired_at      datetimeoffset(7) NULL,
    CONSTRAINT pk_beds PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_beds_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT uq_beds_code_per_ward UNIQUE (ward_id, code),
    CONSTRAINT ck_beds_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(bed_type)) <> N''),
    CONSTRAINT ck_beds_lifecycle CHECK (retired_at IS NULL OR retired_at >= available_from)
);

CREATE TABLE dbo.ward_capacity_periods (
    id                  uniqueidentifier NOT NULL,
    ward_id             uniqueidentifier NOT NULL,
    starts_at           datetimeoffset(7) NOT NULL,
    ends_at             datetimeoffset(7) NULL,
    staffed_bed_limit   int NOT NULL,
    CONSTRAINT pk_ward_capacity_periods PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_ward_capacity_periods_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT ck_ward_capacity_periods_limit CHECK (staffed_bed_limit >= 0),
    CONSTRAINT ck_ward_capacity_periods_time CHECK (ends_at IS NULL OR ends_at > starts_at)
);

CREATE TABLE dbo.bed_blocks (
    id          uniqueidentifier NOT NULL,
    bed_id      uniqueidentifier NOT NULL,
    starts_at   datetimeoffset(7) NOT NULL,
    ends_at     datetimeoffset(7) NULL,
    reason      nvarchar(500) NOT NULL,
    CONSTRAINT pk_bed_blocks PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_bed_blocks_bed FOREIGN KEY (bed_id) REFERENCES dbo.beds (id),
    CONSTRAINT ck_bed_blocks_reason CHECK (LTRIM(RTRIM(reason)) <> N''),
    CONSTRAINT ck_bed_blocks_time CHECK (ends_at IS NULL OR ends_at > starts_at)
);

CREATE TABLE dbo.treatment_types (
    id                       uniqueidentifier NOT NULL,
    code                     nvarchar(100) NOT NULL,
    name                     nvarchar(200) NOT NULL,
    description              nvarchar(1000) NULL,
    default_duration_minutes int NOT NULL,
    requires_bed             bit NOT NULL CONSTRAINT df_treatment_types_requires_bed DEFAULT (0),
    required_bed_type        nvarchar(100) NULL,
    CONSTRAINT pk_treatment_types PRIMARY KEY CLUSTERED (id),
    CONSTRAINT uq_treatment_types_code UNIQUE (code),
    CONSTRAINT ck_treatment_types_text CHECK (LTRIM(RTRIM(code)) <> N'' AND LTRIM(RTRIM(name)) <> N''),
    CONSTRAINT ck_treatment_types_duration CHECK (default_duration_minutes > 0),
    CONSTRAINT ck_treatment_types_bed_type CHECK (required_bed_type IS NULL OR LTRIM(RTRIM(required_bed_type)) <> N'')
);

CREATE TABLE dbo.ward_treatment_capabilities (
    id                         uniqueidentifier NOT NULL,
    ward_id                    uniqueidentifier NOT NULL,
    treatment_type_id          uniqueidentifier NOT NULL,
    starts_at                  datetimeoffset(7) NOT NULL,
    ends_at                    datetimeoffset(7) NULL,
    max_concurrent             int NOT NULL,
    duration_override_minutes  int NULL,
    CONSTRAINT pk_ward_treatment_capabilities PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_ward_treatment_capabilities_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT fk_ward_treatment_capabilities_type FOREIGN KEY (treatment_type_id) REFERENCES dbo.treatment_types (id),
    CONSTRAINT ck_ward_treatment_capabilities_limit CHECK (max_concurrent >= 0),
    CONSTRAINT ck_ward_treatment_capabilities_duration CHECK (
        duration_override_minutes IS NULL OR duration_override_minutes > 0
    ),
    CONSTRAINT ck_ward_treatment_capabilities_time CHECK (ends_at IS NULL OR ends_at > starts_at)
);

CREATE TABLE dbo.admissions (
    id                    uniqueidentifier NOT NULL,
    patient_id            uniqueidentifier NOT NULL,
    requested_at          datetimeoffset(7) NOT NULL,
    admitted_at           datetimeoffset(7) NULL,
    expected_discharge_at datetimeoffset(7) NULL,
    discharged_at         datetimeoffset(7) NULL,
    cancelled_at          datetimeoffset(7) NULL,
    priority              int NOT NULL CONSTRAINT df_admissions_priority DEFAULT (0),
    concurrency_version   bigint NOT NULL CONSTRAINT df_admissions_concurrency_version DEFAULT (0),
    CONSTRAINT pk_admissions PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_admissions_patient FOREIGN KEY (patient_id) REFERENCES dbo.patients (id),
    CONSTRAINT ck_admissions_priority CHECK (priority >= 0),
    CONSTRAINT ck_admissions_concurrency_version CHECK (concurrency_version >= 0),
    CONSTRAINT ck_admissions_times CHECK (
        (admitted_at IS NULL OR admitted_at >= requested_at)
        AND (expected_discharge_at IS NULL OR expected_discharge_at >= requested_at)
        AND (expected_discharge_at IS NULL OR admitted_at IS NULL OR expected_discharge_at >= admitted_at)
        AND (discharged_at IS NULL OR (admitted_at IS NOT NULL AND discharged_at >= admitted_at))
        AND (cancelled_at IS NULL OR cancelled_at >= requested_at)
        AND NOT (cancelled_at IS NOT NULL AND discharged_at IS NOT NULL)
        AND NOT (cancelled_at IS NOT NULL AND admitted_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX ux_admissions_one_open_per_patient
    ON dbo.admissions (patient_id)
    WHERE discharged_at IS NULL AND cancelled_at IS NULL;

CREATE TABLE dbo.treatment_orders (
    id                       uniqueidentifier NOT NULL,
    admission_id             uniqueidentifier NOT NULL,
    treatment_type_id        uniqueidentifier NOT NULL,
    ordered_at               datetimeoffset(7) NOT NULL,
    ready_at                 datetimeoffset(7) NOT NULL,
    planned_duration_minutes int NOT NULL,
    priority                 int NOT NULL CONSTRAINT df_treatment_orders_priority DEFAULT (0),
    cancelled_at             datetimeoffset(7) NULL,
    concurrency_version      bigint NOT NULL CONSTRAINT df_treatment_orders_concurrency_version DEFAULT (0),
    CONSTRAINT pk_treatment_orders PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_treatment_orders_admission FOREIGN KEY (admission_id) REFERENCES dbo.admissions (id),
    CONSTRAINT fk_treatment_orders_type FOREIGN KEY (treatment_type_id) REFERENCES dbo.treatment_types (id),
    CONSTRAINT uq_treatment_orders_id_admission UNIQUE (id, admission_id),
    CONSTRAINT ck_treatment_orders_duration CHECK (planned_duration_minutes > 0),
    CONSTRAINT ck_treatment_orders_priority CHECK (priority >= 0),
    CONSTRAINT ck_treatment_orders_concurrency_version CHECK (concurrency_version >= 0),
    CONSTRAINT ck_treatment_orders_times CHECK (ready_at >= ordered_at AND (cancelled_at IS NULL OR cancelled_at >= ordered_at))
);

CREATE TABLE dbo.treatment_order_conditions (
    treatment_order_id   uniqueidentifier NOT NULL,
    patient_condition_id uniqueidentifier NOT NULL,
    CONSTRAINT pk_treatment_order_conditions PRIMARY KEY CLUSTERED (treatment_order_id, patient_condition_id),
    CONSTRAINT fk_treatment_order_conditions_order FOREIGN KEY (treatment_order_id)
        REFERENCES dbo.treatment_orders (id) ON DELETE CASCADE,
    CONSTRAINT fk_treatment_order_conditions_condition FOREIGN KEY (patient_condition_id)
        REFERENCES dbo.patient_conditions (id)
);

CREATE TABLE dbo.treatment_order_dependencies (
    treatment_order_id    uniqueidentifier NOT NULL,
    prerequisite_order_id uniqueidentifier NOT NULL,
    CONSTRAINT pk_treatment_order_dependencies PRIMARY KEY CLUSTERED (treatment_order_id, prerequisite_order_id),
    CONSTRAINT fk_treatment_order_dependencies_order FOREIGN KEY (treatment_order_id)
        REFERENCES dbo.treatment_orders (id) ON DELETE CASCADE,
    CONSTRAINT fk_treatment_order_dependencies_prerequisite FOREIGN KEY (prerequisite_order_id)
        REFERENCES dbo.treatment_orders (id),
    CONSTRAINT ck_treatment_order_dependencies_not_self CHECK (treatment_order_id <> prerequisite_order_id)
);

CREATE TABLE dbo.bed_requests (
    id                  uniqueidentifier NOT NULL,
    admission_id        uniqueidentifier NOT NULL,
    treatment_order_id  uniqueidentifier NULL,
    target_ward_id      uniqueidentifier NULL,
    required_bed_type   nvarchar(100) NULL,
    requested_at        datetimeoffset(7) NOT NULL,
    priority            int NOT NULL CONSTRAINT df_bed_requests_priority DEFAULT (0),
    fulfilled_at        datetimeoffset(7) NULL,
    cancelled_at        datetimeoffset(7) NULL,
    concurrency_version bigint NOT NULL CONSTRAINT df_bed_requests_concurrency_version DEFAULT (0),
    CONSTRAINT pk_bed_requests PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_bed_requests_admission FOREIGN KEY (admission_id) REFERENCES dbo.admissions (id),
    CONSTRAINT fk_bed_requests_order FOREIGN KEY (treatment_order_id) REFERENCES dbo.treatment_orders (id),
    CONSTRAINT fk_bed_requests_ward FOREIGN KEY (target_ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT ck_bed_requests_bed_type CHECK (required_bed_type IS NULL OR LTRIM(RTRIM(required_bed_type)) <> N''),
    CONSTRAINT ck_bed_requests_priority CHECK (priority >= 0),
    CONSTRAINT ck_bed_requests_concurrency_version CHECK (concurrency_version >= 0),
    CONSTRAINT ck_bed_requests_times CHECK (
        (fulfilled_at IS NULL OR fulfilled_at >= requested_at)
        AND (cancelled_at IS NULL OR cancelled_at >= requested_at)
        AND NOT (fulfilled_at IS NOT NULL AND cancelled_at IS NOT NULL)
    )
);

CREATE TABLE dbo.bed_stays (
    id                  uniqueidentifier NOT NULL,
    admission_id        uniqueidentifier NOT NULL,
    bed_id              uniqueidentifier NOT NULL,
    bed_request_id      uniqueidentifier NULL,
    started_at          datetimeoffset(7) NOT NULL,
    expected_end_at     datetimeoffset(7) NULL,
    ended_at            datetimeoffset(7) NULL,
    end_reason          nvarchar(200) NULL,
    concurrency_version bigint NOT NULL CONSTRAINT df_bed_stays_concurrency_version DEFAULT (0),
    CONSTRAINT pk_bed_stays PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_bed_stays_admission FOREIGN KEY (admission_id) REFERENCES dbo.admissions (id),
    CONSTRAINT fk_bed_stays_bed FOREIGN KEY (bed_id) REFERENCES dbo.beds (id),
    CONSTRAINT fk_bed_stays_request FOREIGN KEY (bed_request_id) REFERENCES dbo.bed_requests (id),
    CONSTRAINT ck_bed_stays_concurrency_version CHECK (concurrency_version >= 0),
    CONSTRAINT ck_bed_stays_times CHECK (
        (expected_end_at IS NULL OR expected_end_at >= started_at)
        AND (ended_at IS NULL OR ended_at >= started_at)
        AND (end_reason IS NULL OR LTRIM(RTRIM(end_reason)) <> N'')
    )
);

CREATE TABLE dbo.treatment_sessions (
    id                  uniqueidentifier NOT NULL,
    treatment_order_id  uniqueidentifier NOT NULL,
    ward_id             uniqueidentifier NOT NULL,
    bed_stay_id         uniqueidentifier NULL,
    started_at          datetimeoffset(7) NOT NULL,
    expected_end_at     datetimeoffset(7) NOT NULL,
    ended_at            datetimeoffset(7) NULL,
    outcome             nvarchar(100) NULL,
    concurrency_version bigint NOT NULL CONSTRAINT df_treatment_sessions_concurrency_version DEFAULT (0),
    CONSTRAINT pk_treatment_sessions PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_treatment_sessions_order FOREIGN KEY (treatment_order_id) REFERENCES dbo.treatment_orders (id),
    CONSTRAINT fk_treatment_sessions_ward FOREIGN KEY (ward_id) REFERENCES dbo.wards (id),
    CONSTRAINT fk_treatment_sessions_stay FOREIGN KEY (bed_stay_id) REFERENCES dbo.bed_stays (id),
    CONSTRAINT ck_treatment_sessions_concurrency_version CHECK (concurrency_version >= 0),
    CONSTRAINT ck_treatment_sessions_outcome CHECK (outcome IS NULL OR LTRIM(RTRIM(outcome)) <> N''),
    CONSTRAINT ck_treatment_sessions_times CHECK (
        expected_end_at >= started_at AND (ended_at IS NULL OR ended_at >= started_at)
    )
);

CREATE TABLE dbo.flow_events (
    id             uniqueidentifier NOT NULL,
    admission_id   uniqueidentifier NULL,
    event_type     nvarchar(100) NOT NULL,
    occurred_at    datetimeoffset(7) NOT NULL,
    recorded_at    datetimeoffset(7) NOT NULL,
    correlation_id nvarchar(200) NOT NULL,
    payload_json   nvarchar(max) NOT NULL CONSTRAINT df_flow_events_payload DEFAULT (N'{}'),
    CONSTRAINT pk_flow_events PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_flow_events_admission FOREIGN KEY (admission_id) REFERENCES dbo.admissions (id),
    CONSTRAINT ck_flow_events_text CHECK (LTRIM(RTRIM(event_type)) <> N'' AND LTRIM(RTRIM(correlation_id)) <> N''),
    CONSTRAINT ck_flow_events_payload CHECK (
        ISJSON(payload_json) = 1 AND LEFT(LTRIM(payload_json), 1) = N'{'
    )
);

-- Scenario state is intentionally separate from operational hospital state.
CREATE TABLE dbo.simulation_scenarios (
    id          uniqueidentifier NOT NULL,
    name        nvarchar(200) NOT NULL,
    description nvarchar(1000) NULL,
    created_at  datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_simulation_scenarios PRIMARY KEY CLUSTERED (id),
    CONSTRAINT ck_simulation_scenarios_name CHECK (LTRIM(RTRIM(name)) <> N'')
);

CREATE TABLE dbo.simulation_scenario_versions (
    id              uniqueidentifier NOT NULL,
    scenario_id     uniqueidentifier NOT NULL,
    version         int NOT NULL,
    parameters_json nvarchar(max) NOT NULL,
    created_at      datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_simulation_scenario_versions PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_simulation_scenario_versions_scenario FOREIGN KEY (scenario_id)
        REFERENCES dbo.simulation_scenarios (id),
    CONSTRAINT uq_simulation_scenario_versions_version UNIQUE (scenario_id, version),
    CONSTRAINT ck_simulation_scenario_versions_version CHECK (version > 0),
    CONSTRAINT ck_simulation_scenario_versions_parameters CHECK (
        ISJSON(parameters_json) = 1 AND LEFT(LTRIM(parameters_json), 1) = N'{'
    )
);

CREATE TABLE dbo.simulation_snapshots (
    id             uniqueidentifier NOT NULL,
    captured_at    datetimeoffset(7) NOT NULL,
    schema_version nvarchar(100) NOT NULL,
    state_json     nvarchar(max) NOT NULL,
    CONSTRAINT pk_simulation_snapshots PRIMARY KEY CLUSTERED (id),
    CONSTRAINT ck_simulation_snapshots_version CHECK (LTRIM(RTRIM(schema_version)) <> N''),
    CONSTRAINT ck_simulation_snapshots_state CHECK (
        ISJSON(state_json) = 1 AND LEFT(LTRIM(state_json), 1) = N'{'
    )
);

CREATE TABLE dbo.simulation_runs (
    id                  uniqueidentifier NOT NULL,
    scenario_version_id uniqueidentifier NOT NULL,
    snapshot_id         uniqueidentifier NOT NULL,
    random_seed         bigint NOT NULL,
    engine_version      nvarchar(100) NOT NULL,
    simulation_start_at datetimeoffset(7) NOT NULL,
    simulation_end_at   datetimeoffset(7) NOT NULL,
    started_at          datetimeoffset(7) NULL,
    finished_at         datetimeoffset(7) NULL,
    status              nvarchar(20) NOT NULL,
    CONSTRAINT pk_simulation_runs PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_simulation_runs_scenario_version FOREIGN KEY (scenario_version_id)
        REFERENCES dbo.simulation_scenario_versions (id),
    CONSTRAINT fk_simulation_runs_snapshot FOREIGN KEY (snapshot_id) REFERENCES dbo.simulation_snapshots (id),
    CONSTRAINT ck_simulation_runs_engine CHECK (LTRIM(RTRIM(engine_version)) <> N''),
    CONSTRAINT ck_simulation_runs_status CHECK (status IN (N'queued', N'running', N'completed', N'failed', N'cancelled')),
    CONSTRAINT ck_simulation_runs_simulation_time CHECK (simulation_end_at >= simulation_start_at),
    CONSTRAINT ck_simulation_runs_execution_time CHECK (
        finished_at IS NULL OR (started_at IS NOT NULL AND finished_at >= started_at)
    )
);

CREATE TABLE dbo.simulation_events (
    id           uniqueidentifier NOT NULL,
    run_id       uniqueidentifier NOT NULL,
    sequence_no  bigint NOT NULL,
    simulated_at datetimeoffset(7) NOT NULL,
    event_type   nvarchar(100) NOT NULL,
    entity_type  nvarchar(100) NOT NULL,
    entity_id    uniqueidentifier NOT NULL,
    payload_json nvarchar(max) NOT NULL CONSTRAINT df_simulation_events_payload DEFAULT (N'{}'),
    CONSTRAINT pk_simulation_events PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_simulation_events_run FOREIGN KEY (run_id) REFERENCES dbo.simulation_runs (id) ON DELETE CASCADE,
    CONSTRAINT uq_simulation_events_sequence UNIQUE (run_id, sequence_no),
    CONSTRAINT ck_simulation_events_sequence CHECK (sequence_no > 0),
    CONSTRAINT ck_simulation_events_text CHECK (LTRIM(RTRIM(event_type)) <> N'' AND LTRIM(RTRIM(entity_type)) <> N''),
    CONSTRAINT ck_simulation_events_payload CHECK (
        ISJSON(payload_json) = 1 AND LEFT(LTRIM(payload_json), 1) = N'{'
    )
);

CREATE TABLE dbo.simulation_metric_samples (
    id           uniqueidentifier NOT NULL,
    run_id       uniqueidentifier NOT NULL,
    simulated_at datetimeoffset(7) NOT NULL,
    ward_id      uniqueidentifier NULL,
    metric_name  nvarchar(200) NOT NULL,
    value        decimal(38, 12) NOT NULL,
    unit         nvarchar(50) NOT NULL,
    CONSTRAINT pk_simulation_metric_samples PRIMARY KEY CLUSTERED (id),
    CONSTRAINT fk_simulation_metric_samples_run FOREIGN KEY (run_id) REFERENCES dbo.simulation_runs (id) ON DELETE CASCADE,
    CONSTRAINT ck_simulation_metric_samples_text CHECK (
        LTRIM(RTRIM(metric_name)) <> N'' AND LTRIM(RTRIM(unit)) <> N''
    )
);

CREATE INDEX ix_patient_weight_measurements_patient_time
    ON dbo.patient_weight_measurements (patient_id, measured_at DESC);
CREATE INDEX ix_patient_conditions_patient_onset
    ON dbo.patient_conditions (patient_id, onset_at DESC);
CREATE INDEX ix_beds_ward_availability
    ON dbo.beds (ward_id, available_from);
CREATE INDEX ix_bed_blocks_bed_time
    ON dbo.bed_blocks (bed_id, starts_at);
CREATE INDEX ix_ward_capacity_periods_ward_time
    ON dbo.ward_capacity_periods (ward_id, starts_at);
CREATE INDEX ix_capabilities_ward_treatment_time
    ON dbo.ward_treatment_capabilities (ward_id, treatment_type_id, starts_at);
CREATE INDEX ix_admissions_patient_history
    ON dbo.admissions (patient_id, requested_at DESC);
CREATE INDEX ix_treatment_orders_admission_queue
    ON dbo.treatment_orders (admission_id, priority DESC, ready_at, ordered_at);
CREATE INDEX ix_treatment_order_conditions_condition
    ON dbo.treatment_order_conditions (patient_condition_id);
CREATE INDEX ix_bed_requests_admission_queue
    ON dbo.bed_requests (admission_id, priority DESC, requested_at);
CREATE INDEX ix_bed_requests_pending
    ON dbo.bed_requests (priority DESC, requested_at)
    WHERE fulfilled_at IS NULL AND cancelled_at IS NULL;
CREATE INDEX ix_bed_stays_bed_history
    ON dbo.bed_stays (bed_id, started_at DESC);
CREATE INDEX ix_bed_stays_admission_history
    ON dbo.bed_stays (admission_id, started_at DESC);
CREATE INDEX ix_treatment_sessions_order_time
    ON dbo.treatment_sessions (treatment_order_id, started_at);
CREATE INDEX ix_flow_events_admission_time
    ON dbo.flow_events (admission_id, occurred_at);
CREATE INDEX ix_flow_events_correlation
    ON dbo.flow_events (correlation_id, recorded_at);
CREATE INDEX ix_simulation_events_run_time
    ON dbo.simulation_events (run_id, simulated_at, sequence_no);
CREATE INDEX ix_simulation_metric_samples_run_time
    ON dbo.simulation_metric_samples (run_id, simulated_at);
CREATE INDEX ix_simulation_metric_samples_ward_metric
    ON dbo.simulation_metric_samples (run_id, ward_id, metric_name, simulated_at);

GO

-- SQL Server has no exclusion constraints. These triggers enforce the same
-- half-open interval rule ([start, end)) while taking update locks so a race
-- between two writes cannot create overlapping periods or occupancy.
CREATE TRIGGER dbo.trg_ward_capacity_periods_no_overlap
ON dbo.ward_capacity_periods
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.ward_capacity_periods p WITH (UPDLOCK, HOLDLOCK)
            ON p.ward_id = i.ward_id
           AND p.id <> i.id
           AND i.starts_at < ISNULL(p.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
           AND p.starts_at < ISNULL(i.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
    )
        THROW 51000, 'Ward capacity periods cannot overlap.', 1;
END;
GO

CREATE TRIGGER dbo.trg_ward_treatment_capabilities_no_overlap
ON dbo.ward_treatment_capabilities
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.ward_treatment_capabilities c WITH (UPDLOCK, HOLDLOCK)
            ON c.ward_id = i.ward_id
           AND c.treatment_type_id = i.treatment_type_id
           AND c.id <> i.id
           AND i.starts_at < ISNULL(c.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
           AND c.starts_at < ISNULL(i.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
    )
        THROW 51001, 'Ward treatment capability periods cannot overlap.', 1;
END;
GO

CREATE TRIGGER dbo.trg_bed_stays_no_overlap
ON dbo.bed_stays
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.bed_stays s WITH (UPDLOCK, HOLDLOCK)
            ON s.id <> i.id
           AND (
                s.bed_id = i.bed_id
                OR s.admission_id = i.admission_id
           )
           AND i.started_at < ISNULL(s.ended_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
           AND s.started_at < ISNULL(i.ended_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
    )
        THROW 51002, 'Bed stays cannot overlap for the same bed or admission.', 1;
END;
GO

CREATE TRIGGER dbo.trg_prevent_blocking_occupied_bed
ON dbo.bed_blocks
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.bed_stays s WITH (UPDLOCK, HOLDLOCK) ON s.bed_id = i.bed_id
         WHERE s.started_at < ISNULL(i.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
           AND i.starts_at < ISNULL(s.ended_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
    )
        THROW 51003, 'A bed cannot be blocked during an occupied interval.', 1;
END;
GO

CREATE TRIGGER dbo.trg_prevent_occupying_blocked_bed
ON dbo.bed_stays
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.bed_blocks b WITH (UPDLOCK, HOLDLOCK) ON b.bed_id = i.bed_id
         WHERE b.starts_at < ISNULL(i.ended_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
           AND i.started_at < ISNULL(b.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59.9999999 +00:00'))
    )
        THROW 51004, 'A bed cannot be occupied during a blocked interval.', 1;
END;
GO

CREATE TRIGGER dbo.trg_validate_treatment_order_condition_patient
ON dbo.treatment_order_conditions
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
          JOIN dbo.admissions a ON a.id = o.admission_id
          JOIN dbo.patient_conditions c ON c.id = i.patient_condition_id
         WHERE a.patient_id <> c.patient_id
    )
        THROW 51005, 'A treatment order condition must belong to the admission patient.', 1;
END;
GO

CREATE TRIGGER dbo.trg_validate_treatment_order_dependency
ON dbo.treatment_order_dependencies
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders dependent_order ON dependent_order.id = i.treatment_order_id
          JOIN dbo.treatment_orders prerequisite_order ON prerequisite_order.id = i.prerequisite_order_id
         WHERE dependent_order.admission_id <> prerequisite_order.admission_id
    )
        THROW 51006, 'Treatment dependencies must stay within one admission.', 1;

    DECLARE @has_cycle bit = 0;

    ;WITH dependency_chain AS (
        SELECT
            i.treatment_order_id AS root_order_id,
            i.treatment_order_id,
            i.prerequisite_order_id,
            CONVERT(varchar(max), '|' + CONVERT(varchar(36), i.treatment_order_id) + '|'
                + CONVERT(varchar(36), i.prerequisite_order_id) + '|') AS visited_path,
            CONVERT(bit, 0) AS is_cycle
          FROM inserted i
        UNION ALL
        SELECT
            c.root_order_id,
            d.treatment_order_id,
            d.prerequisite_order_id,
            CONVERT(varchar(max), c.visited_path + CONVERT(varchar(36), d.prerequisite_order_id) + '|'),
            CONVERT(bit, CASE
                WHEN c.visited_path LIKE '%|' + CONVERT(varchar(36), d.prerequisite_order_id) + '|%'
                    THEN 1 ELSE 0 END)
          FROM dependency_chain c
          JOIN dbo.treatment_order_dependencies d
            ON d.treatment_order_id = c.prerequisite_order_id
         WHERE c.is_cycle = 0
    )
    SELECT TOP (1) @has_cycle = 1
      FROM dependency_chain
     WHERE is_cycle = 1
    OPTION (MAXRECURSION 32767);

    IF @has_cycle = 1
        THROW 51007, 'Treatment dependencies cannot form a cycle.', 1;
END;
GO

CREATE TRIGGER dbo.trg_validate_bed_request_relationships
ON dbo.bed_requests
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
         WHERE o.admission_id <> i.admission_id
    )
        THROW 51008, 'A bed request treatment order must belong to the same admission.', 1;
END;
GO

CREATE TRIGGER dbo.trg_validate_bed_stay_relationships
ON dbo.bed_stays
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.bed_requests r ON r.id = i.bed_request_id
         WHERE r.admission_id <> i.admission_id
    )
        THROW 51009, 'A bed stay request must belong to the same admission.', 1;
END;
GO

CREATE TRIGGER dbo.trg_validate_treatment_session
ON dbo.treatment_sessions
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
         WHERE o.cancelled_at IS NOT NULL
    )
        THROW 51010, 'A cancelled treatment order cannot start a session.', 1;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_order_dependencies d ON d.treatment_order_id = i.treatment_order_id
         WHERE NOT EXISTS (
             SELECT 1
               FROM dbo.treatment_sessions prerequisite_session
              WHERE prerequisite_session.treatment_order_id = d.prerequisite_order_id
                AND prerequisite_session.outcome = N'completed'
         )
    )
        THROW 51011, 'A treatment order has an incomplete prerequisite.', 1;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
         WHERE NOT EXISTS (
             SELECT 1
               FROM dbo.ward_treatment_capabilities c WITH (UPDLOCK, HOLDLOCK)
              WHERE c.ward_id = i.ward_id
                AND c.treatment_type_id = o.treatment_type_id
                AND c.starts_at <= i.started_at
                AND (c.ends_at IS NULL OR i.expected_end_at <= c.ends_at)
         )
    )
        THROW 51012, 'A treatment session requires an active ward capability.', 1;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
          JOIN dbo.treatment_types t ON t.id = o.treatment_type_id
         WHERE t.requires_bed = 1 AND i.bed_stay_id IS NULL
    )
        THROW 51013, 'This treatment session requires a bed stay.', 1;

    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN dbo.treatment_orders o ON o.id = i.treatment_order_id
          JOIN dbo.bed_stays s ON s.id = i.bed_stay_id
          JOIN dbo.beds b ON b.id = s.bed_id
         WHERE s.admission_id <> o.admission_id
            OR b.ward_id <> i.ward_id
            OR s.started_at > i.started_at
            OR (s.ended_at IS NOT NULL AND i.expected_end_at > s.ended_at)
            OR (i.ended_at IS NOT NULL AND s.ended_at IS NOT NULL AND i.ended_at > s.ended_at)
            OR (i.ended_at IS NULL AND s.ended_at IS NOT NULL)
    )
        THROW 51014, 'A treatment session bed stay must match and cover its interval.', 1;
END;
GO

-- Flow events, scenario versions, snapshots and simulation events are
-- immutable records once written.
CREATE TRIGGER dbo.trg_flow_events_append_only
ON dbo.flow_events
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51015, 'flow_events is append-only.', 1;
END;
GO

CREATE TRIGGER dbo.trg_simulation_scenario_versions_immutable
ON dbo.simulation_scenario_versions
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51016, 'Simulation scenario versions are immutable.', 1;
END;
GO

CREATE TRIGGER dbo.trg_simulation_snapshots_immutable
ON dbo.simulation_snapshots
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51017, 'Simulation snapshots are immutable.', 1;
END;
GO

CREATE TRIGGER dbo.trg_simulation_events_append_only
ON dbo.simulation_events
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51018, 'Simulation events are append-only.', 1;
END;
GO

COMMIT TRANSACTION;
