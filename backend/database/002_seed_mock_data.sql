-- Alcidion hospital database
-- Migration: 002_seed_mock_data
-- Dialect: Microsoft SQL Server 2016+
-- Requires: 001_initial_hospital_schema.sql

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

DECLARE @pAda uniqueidentifier = '10000000-0000-0000-0000-000000000001';
DECLARE @pGrace uniqueidentifier = '10000000-0000-0000-0000-000000000002';
DECLARE @pAlan uniqueidentifier = '10000000-0000-0000-0000-000000000003';

DECLARE @wtAdaInitial uniqueidentifier = '11000000-0000-0000-0000-000000000001';
DECLARE @wtAdaFollowup uniqueidentifier = '11000000-0000-0000-0000-000000000002';
DECLARE @wtGrace uniqueidentifier = '11000000-0000-0000-0000-000000000003';
DECLARE @wtAlan uniqueidentifier = '11000000-0000-0000-0000-000000000004';

DECLARE @ctRespiratory uniqueidentifier = '20000000-0000-0000-0000-000000000001';
DECLARE @ctCardiac uniqueidentifier = '20000000-0000-0000-0000-000000000002';
DECLARE @ctInfection uniqueidentifier = '20000000-0000-0000-0000-000000000003';

DECLARE @conditionAda uniqueidentifier = '21000000-0000-0000-0000-000000000001';
DECLARE @conditionGrace uniqueidentifier = '21000000-0000-0000-0000-000000000002';
DECLARE @conditionAlan uniqueidentifier = '21000000-0000-0000-0000-000000000003';

DECLARE @wEd uniqueidentifier = '30000000-0000-0000-0000-000000000001';
DECLARE @wGeneral uniqueidentifier = '30000000-0000-0000-0000-000000000002';
DECLARE @wIcu uniqueidentifier = '30000000-0000-0000-0000-000000000003';

DECLARE @bedEd01 uniqueidentifier = '31000000-0000-0000-0000-000000000001';
DECLARE @bedGeneral01 uniqueidentifier = '31000000-0000-0000-0000-000000000002';
DECLARE @bedGeneral02 uniqueidentifier = '31000000-0000-0000-0000-000000000003';
DECLARE @bedIcu01 uniqueidentifier = '31000000-0000-0000-0000-000000000004';

DECLARE @ttAssessment uniqueidentifier = '40000000-0000-0000-0000-000000000001';
DECLARE @ttOxygen uniqueidentifier = '40000000-0000-0000-0000-000000000002';
DECLARE @ttPhysio uniqueidentifier = '40000000-0000-0000-0000-000000000003';

DECLARE @admissionAda uniqueidentifier = '50000000-0000-0000-0000-000000000001';
DECLARE @admissionGrace uniqueidentifier = '50000000-0000-0000-0000-000000000002';
DECLARE @admissionAlan uniqueidentifier = '50000000-0000-0000-0000-000000000003';

DECLARE @orderAdaAssessment uniqueidentifier = '60000000-0000-0000-0000-000000000001';
DECLARE @orderAdaOxygen uniqueidentifier = '60000000-0000-0000-0000-000000000002';
DECLARE @orderGracePhysio uniqueidentifier = '60000000-0000-0000-0000-000000000003';
DECLARE @orderAlanAssessment uniqueidentifier = '60000000-0000-0000-0000-000000000004';

DECLARE @requestAda uniqueidentifier = '70000000-0000-0000-0000-000000000001';
DECLARE @requestGrace uniqueidentifier = '70000000-0000-0000-0000-000000000002';
DECLARE @requestAlan uniqueidentifier = '70000000-0000-0000-0000-000000000003';

DECLARE @stayAda uniqueidentifier = '71000000-0000-0000-0000-000000000001';
DECLARE @stayGrace uniqueidentifier = '71000000-0000-0000-0000-000000000002';

DECLARE @sessionAdaAssessment uniqueidentifier = '72000000-0000-0000-0000-000000000001';
DECLARE @sessionAdaOxygen uniqueidentifier = '72000000-0000-0000-0000-000000000002';
DECLARE @sessionGracePhysio uniqueidentifier = '72000000-0000-0000-0000-000000000003';

DECLARE @scenario uniqueidentifier = '80000000-0000-0000-0000-000000000001';
DECLARE @scenarioVersion uniqueidentifier = '81000000-0000-0000-0000-000000000001';
DECLARE @snapshot uniqueidentifier = '82000000-0000-0000-0000-000000000001';
DECLARE @run uniqueidentifier = '83000000-0000-0000-0000-000000000001';

INSERT INTO dbo.patients (id, mrn, given_name, family_name, date_of_birth, gender, registered_at)
SELECT @pAda, N'ALC-0001', N'Ada', N'Lovelace', '1815-12-10', N'female', '2026-09-10 07:45:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patients WHERE id = @pAda);

INSERT INTO dbo.patients (id, mrn, given_name, family_name, date_of_birth, gender, registered_at)
SELECT @pGrace, N'ALC-0002', N'Grace', N'Hopper', '1906-12-09', N'female', '2026-09-08 07:30:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patients WHERE id = @pGrace);

INSERT INTO dbo.patients (id, mrn, given_name, family_name, date_of_birth, gender, registered_at)
SELECT @pAlan, N'ALC-0003', N'Alan', N'Turing', '1912-06-23', N'male', '2026-09-11 14:45:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patients WHERE id = @pAlan);

INSERT INTO dbo.patient_weight_measurements (id, patient_id, weight_kg, measured_at)
SELECT @wtAdaInitial, @pAda, 64.200, '2026-09-10 07:50:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_weight_measurements WHERE id = @wtAdaInitial);

INSERT INTO dbo.patient_weight_measurements (id, patient_id, weight_kg, measured_at)
SELECT @wtAdaFollowup, @pAda, 63.800, '2026-09-11 08:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_weight_measurements WHERE id = @wtAdaFollowup);

INSERT INTO dbo.patient_weight_measurements (id, patient_id, weight_kg, measured_at)
SELECT @wtGrace, @pGrace, 71.500, '2026-09-08 07:35:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_weight_measurements WHERE id = @wtGrace);

INSERT INTO dbo.patient_weight_measurements (id, patient_id, weight_kg, measured_at)
SELECT @wtAlan, @pAlan, 78.100, '2026-09-11 14:50:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_weight_measurements WHERE id = @wtAlan);

INSERT INTO dbo.condition_types (id, code, name, description)
SELECT @ctRespiratory, N'RESP', N'Respiratory distress', N'Synthetic respiratory condition for flow simulations.'
WHERE NOT EXISTS (SELECT 1 FROM dbo.condition_types WHERE id = @ctRespiratory);

INSERT INTO dbo.condition_types (id, code, name, description)
SELECT @ctCardiac, N'CARD', N'Cardiac observation', N'Synthetic cardiac condition for flow simulations.'
WHERE NOT EXISTS (SELECT 1 FROM dbo.condition_types WHERE id = @ctCardiac);

INSERT INTO dbo.condition_types (id, code, name, description)
SELECT @ctInfection, N'INF', N'Infection monitoring', N'Synthetic infection condition for flow simulations.'
WHERE NOT EXISTS (SELECT 1 FROM dbo.condition_types WHERE id = @ctInfection);

INSERT INTO dbo.patient_conditions (id, patient_id, condition_type_id, onset_at, recorded_at, expected_resolved_at, resolved_at, severity)
SELECT @conditionAda, @pAda, @ctRespiratory, '2026-09-10 08:00:00 +00:00', '2026-09-10 08:05:00 +00:00', '2026-09-12 08:00:00 +00:00', NULL, N'moderate'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_conditions WHERE id = @conditionAda);

INSERT INTO dbo.patient_conditions (id, patient_id, condition_type_id, onset_at, recorded_at, expected_resolved_at, resolved_at, severity)
SELECT @conditionGrace, @pGrace, @ctCardiac, '2026-09-08 08:00:00 +00:00', '2026-09-08 08:10:00 +00:00', '2026-09-11 10:00:00 +00:00', '2026-09-11 09:30:00 +00:00', N'mild'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_conditions WHERE id = @conditionGrace);

INSERT INTO dbo.patient_conditions (id, patient_id, condition_type_id, onset_at, recorded_at, expected_resolved_at, resolved_at, severity)
SELECT @conditionAlan, @pAlan, @ctInfection, '2026-09-11 15:00:00 +00:00', '2026-09-11 15:05:00 +00:00', '2026-09-14 15:00:00 +00:00', NULL, N'low'
WHERE NOT EXISTS (SELECT 1 FROM dbo.patient_conditions WHERE id = @conditionAlan);

INSERT INTO dbo.wards (id, code, name, ward_type)
SELECT @wEd, N'ED', N'Emergency Department', N'emergency'
WHERE NOT EXISTS (SELECT 1 FROM dbo.wards WHERE id = @wEd);

INSERT INTO dbo.wards (id, code, name, ward_type)
SELECT @wGeneral, N'GEN', N'General Ward', N'general'
WHERE NOT EXISTS (SELECT 1 FROM dbo.wards WHERE id = @wGeneral);

INSERT INTO dbo.wards (id, code, name, ward_type)
SELECT @wIcu, N'ICU', N'Intensive Care Unit', N'icu'
WHERE NOT EXISTS (SELECT 1 FROM dbo.wards WHERE id = @wIcu);

INSERT INTO dbo.beds (id, ward_id, code, bed_type, available_from)
SELECT @bedEd01, @wEd, N'ED-01', N'assessment', '2026-01-01 00:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.beds WHERE id = @bedEd01);

INSERT INTO dbo.beds (id, ward_id, code, bed_type, available_from)
SELECT @bedGeneral01, @wGeneral, N'GEN-01', N'general', '2026-01-01 00:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.beds WHERE id = @bedGeneral01);

INSERT INTO dbo.beds (id, ward_id, code, bed_type, available_from)
SELECT @bedGeneral02, @wGeneral, N'GEN-02', N'general', '2026-01-01 00:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.beds WHERE id = @bedGeneral02);

INSERT INTO dbo.beds (id, ward_id, code, bed_type, available_from)
SELECT @bedIcu01, @wIcu, N'ICU-01', N'icu', '2026-01-01 00:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.beds WHERE id = @bedIcu01);

INSERT INTO dbo.ward_capacity_periods (id, ward_id, starts_at, staffed_bed_limit)
SELECT '32000000-0000-0000-0000-000000000001', @wEd, '2026-01-01 00:00:00 +00:00', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_capacity_periods WHERE id = '32000000-0000-0000-0000-000000000001');

INSERT INTO dbo.ward_capacity_periods (id, ward_id, starts_at, staffed_bed_limit)
SELECT '32000000-0000-0000-0000-000000000002', @wGeneral, '2026-01-01 00:00:00 +00:00', 2
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_capacity_periods WHERE id = '32000000-0000-0000-0000-000000000002');

INSERT INTO dbo.ward_capacity_periods (id, ward_id, starts_at, staffed_bed_limit)
SELECT '32000000-0000-0000-0000-000000000003', @wIcu, '2026-01-01 00:00:00 +00:00', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_capacity_periods WHERE id = '32000000-0000-0000-0000-000000000003');

INSERT INTO dbo.bed_blocks (id, bed_id, starts_at, ends_at, reason)
SELECT '33000000-0000-0000-0000-000000000001', @bedGeneral02, '2026-09-11 06:00:00 +00:00', '2026-09-11 10:00:00 +00:00', N'cleaning'
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_blocks WHERE id = '33000000-0000-0000-0000-000000000001');

INSERT INTO dbo.treatment_types (id, code, name, description, default_duration_minutes, requires_bed, required_bed_type)
SELECT @ttAssessment, N'ASSESS', N'Initial assessment', N'Non-bed treatment used for intake assessment.', 60, 0, NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_types WHERE id = @ttAssessment);

INSERT INTO dbo.treatment_types (id, code, name, description, default_duration_minutes, requires_bed, required_bed_type)
SELECT @ttOxygen, N'OXYGEN', N'Oxygen support', N'Bed-required synthetic treatment.', 45, 1, N'icu'
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_types WHERE id = @ttOxygen);

INSERT INTO dbo.treatment_types (id, code, name, description, default_duration_minutes, requires_bed, required_bed_type)
SELECT @ttPhysio, N'PHYSIO', N'Physiotherapy', N'Bed-required synthetic treatment.', 60, 1, N'general'
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_types WHERE id = @ttPhysio);

INSERT INTO dbo.ward_treatment_capabilities (id, ward_id, treatment_type_id, starts_at, max_concurrent)
SELECT '41000000-0000-0000-0000-000000000001', @wEd, @ttAssessment, '2026-01-01 00:00:00 +00:00', 4
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_treatment_capabilities WHERE id = '41000000-0000-0000-0000-000000000001');

INSERT INTO dbo.ward_treatment_capabilities (id, ward_id, treatment_type_id, starts_at, max_concurrent)
SELECT '41000000-0000-0000-0000-000000000002', @wIcu, @ttOxygen, '2026-01-01 00:00:00 +00:00', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_treatment_capabilities WHERE id = '41000000-0000-0000-0000-000000000002');

INSERT INTO dbo.ward_treatment_capabilities (id, ward_id, treatment_type_id, starts_at, max_concurrent)
SELECT '41000000-0000-0000-0000-000000000003', @wGeneral, @ttPhysio, '2026-01-01 00:00:00 +00:00', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ward_treatment_capabilities WHERE id = '41000000-0000-0000-0000-000000000003');

INSERT INTO dbo.admissions (id, patient_id, requested_at, admitted_at, expected_discharge_at, priority)
SELECT @admissionAda, @pAda, '2026-09-10 08:00:00 +00:00', '2026-09-10 08:30:00 +00:00', '2026-09-12 08:00:00 +00:00', 2
WHERE NOT EXISTS (SELECT 1 FROM dbo.admissions WHERE id = @admissionAda);

INSERT INTO dbo.admissions (id, patient_id, requested_at, admitted_at, expected_discharge_at, discharged_at, priority)
SELECT @admissionGrace, @pGrace, '2026-09-08 08:00:00 +00:00', '2026-09-08 08:20:00 +00:00', '2026-09-11 10:00:00 +00:00', '2026-09-11 09:45:00 +00:00', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.admissions WHERE id = @admissionGrace);

INSERT INTO dbo.admissions (id, patient_id, requested_at, priority)
SELECT @admissionAlan, @pAlan, '2026-09-11 15:00:00 +00:00', 3
WHERE NOT EXISTS (SELECT 1 FROM dbo.admissions WHERE id = @admissionAlan);

INSERT INTO dbo.treatment_orders (id, admission_id, treatment_type_id, ordered_at, ready_at, planned_duration_minutes, priority)
SELECT @orderAdaAssessment, @admissionAda, @ttAssessment, '2026-09-10 08:35:00 +00:00', '2026-09-10 08:35:00 +00:00', 60, 2
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_orders WHERE id = @orderAdaAssessment);

INSERT INTO dbo.treatment_orders (id, admission_id, treatment_type_id, ordered_at, ready_at, planned_duration_minutes, priority)
SELECT @orderAdaOxygen, @admissionAda, @ttOxygen, '2026-09-10 09:30:00 +00:00', '2026-09-10 09:45:00 +00:00', 45, 3
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_orders WHERE id = @orderAdaOxygen);

INSERT INTO dbo.treatment_orders (id, admission_id, treatment_type_id, ordered_at, ready_at, planned_duration_minutes, priority)
SELECT @orderGracePhysio, @admissionGrace, @ttPhysio, '2026-09-10 13:00:00 +00:00', '2026-09-10 14:00:00 +00:00', 60, 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_orders WHERE id = @orderGracePhysio);

INSERT INTO dbo.treatment_orders (id, admission_id, treatment_type_id, ordered_at, ready_at, planned_duration_minutes, priority)
SELECT @orderAlanAssessment, @admissionAlan, @ttAssessment, '2026-09-11 15:05:00 +00:00', '2026-09-11 15:10:00 +00:00', 60, 2
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_orders WHERE id = @orderAlanAssessment);

INSERT INTO dbo.treatment_order_conditions (treatment_order_id, patient_condition_id)
SELECT @orderAdaOxygen, @conditionAda
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_order_conditions WHERE treatment_order_id = @orderAdaOxygen AND patient_condition_id = @conditionAda);

INSERT INTO dbo.treatment_order_conditions (treatment_order_id, patient_condition_id)
SELECT @orderGracePhysio, @conditionGrace
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_order_conditions WHERE treatment_order_id = @orderGracePhysio AND patient_condition_id = @conditionGrace);

INSERT INTO dbo.treatment_order_conditions (treatment_order_id, patient_condition_id)
SELECT @orderAlanAssessment, @conditionAlan
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_order_conditions WHERE treatment_order_id = @orderAlanAssessment AND patient_condition_id = @conditionAlan);

INSERT INTO dbo.treatment_order_dependencies (treatment_order_id, prerequisite_order_id)
SELECT @orderAdaOxygen, @orderAdaAssessment
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_order_dependencies WHERE treatment_order_id = @orderAdaOxygen AND prerequisite_order_id = @orderAdaAssessment);

INSERT INTO dbo.bed_requests (id, admission_id, treatment_order_id, target_ward_id, required_bed_type, requested_at, priority, fulfilled_at)
SELECT @requestAda, @admissionAda, @orderAdaOxygen, @wIcu, N'icu', '2026-09-10 09:35:00 +00:00', 3, '2026-09-10 09:40:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_requests WHERE id = @requestAda);

INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, required_bed_type, requested_at, priority, fulfilled_at)
SELECT @requestGrace, @admissionGrace, @wGeneral, N'general', '2026-09-08 08:05:00 +00:00', 1, '2026-09-08 08:10:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_requests WHERE id = @requestGrace);

INSERT INTO dbo.bed_requests (id, admission_id, target_ward_id, required_bed_type, requested_at, priority)
SELECT @requestAlan, @admissionAlan, @wGeneral, N'general', '2026-09-11 15:05:00 +00:00', 3
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_requests WHERE id = @requestAlan);

INSERT INTO dbo.bed_stays (id, admission_id, bed_id, bed_request_id, started_at, expected_end_at, ended_at, end_reason)
SELECT @stayAda, @admissionAda, @bedIcu01, @requestAda, '2026-09-10 09:40:00 +00:00', '2026-09-10 11:00:00 +00:00', '2026-09-10 11:00:00 +00:00', N'transfer'
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_stays WHERE id = @stayAda);

INSERT INTO dbo.bed_stays (id, admission_id, bed_id, bed_request_id, started_at, expected_end_at, ended_at, end_reason)
SELECT @stayGrace, @admissionGrace, @bedGeneral01, @requestGrace, '2026-09-08 08:10:00 +00:00', '2026-09-11 10:00:00 +00:00', '2026-09-11 09:45:00 +00:00', N'discharge'
WHERE NOT EXISTS (SELECT 1 FROM dbo.bed_stays WHERE id = @stayGrace);

INSERT INTO dbo.treatment_sessions (id, treatment_order_id, ward_id, started_at, expected_end_at, ended_at, outcome)
SELECT @sessionAdaAssessment, @orderAdaAssessment, @wEd, '2026-09-10 08:40:00 +00:00', '2026-09-10 09:40:00 +00:00', '2026-09-10 09:35:00 +00:00', N'completed'
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_sessions WHERE id = @sessionAdaAssessment);

INSERT INTO dbo.treatment_sessions (id, treatment_order_id, ward_id, bed_stay_id, started_at, expected_end_at, ended_at, outcome)
SELECT @sessionAdaOxygen, @orderAdaOxygen, @wIcu, @stayAda, '2026-09-10 09:45:00 +00:00', '2026-09-10 10:30:00 +00:00', '2026-09-10 10:30:00 +00:00', N'completed'
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_sessions WHERE id = @sessionAdaOxygen);

INSERT INTO dbo.treatment_sessions (id, treatment_order_id, ward_id, bed_stay_id, started_at, expected_end_at, ended_at, outcome)
SELECT @sessionGracePhysio, @orderGracePhysio, @wGeneral, @stayGrace, '2026-09-10 14:00:00 +00:00', '2026-09-10 15:00:00 +00:00', '2026-09-10 15:00:00 +00:00', N'completed'
WHERE NOT EXISTS (SELECT 1 FROM dbo.treatment_sessions WHERE id = @sessionGracePhysio);

INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT '90000000-0000-0000-0000-000000000001', @admissionAda, N'requested', '2026-09-10 08:00:00 +00:00', '2026-09-10 08:00:01 +00:00', N'mock-ada-001', N'{"source":"seed","stage":"admission"}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.flow_events WHERE id = '90000000-0000-0000-0000-000000000001');

INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT '90000000-0000-0000-0000-000000000002', @admissionAda, N'admitted', '2026-09-10 08:30:00 +00:00', '2026-09-10 08:30:01 +00:00', N'mock-ada-001', N'{"ward":"ICU","bed":"ICU-01"}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.flow_events WHERE id = '90000000-0000-0000-0000-000000000002');

INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT '90000000-0000-0000-0000-000000000003', @admissionAda, N'treatment_completed', '2026-09-10 10:30:00 +00:00', '2026-09-10 10:30:01 +00:00', N'mock-ada-001', N'{"treatment":"OXYGEN","duration_minutes":45}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.flow_events WHERE id = '90000000-0000-0000-0000-000000000003');

INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT '90000000-0000-0000-0000-000000000004', @admissionGrace, N'discharged', '2026-09-11 09:45:00 +00:00', '2026-09-11 09:45:01 +00:00', N'mock-grace-001', N'{"bed":"GEN-01"}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.flow_events WHERE id = '90000000-0000-0000-0000-000000000004');

INSERT INTO dbo.flow_events (id, admission_id, event_type, occurred_at, recorded_at, correlation_id, payload_json)
SELECT '90000000-0000-0000-0000-000000000005', @admissionAlan, N'queued', '2026-09-11 15:05:00 +00:00', '2026-09-11 15:05:01 +00:00', N'mock-alan-001', N'{"queue":"bed","ward":"GEN"}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.flow_events WHERE id = '90000000-0000-0000-0000-000000000005');

INSERT INTO dbo.simulation_scenarios (id, name, description, created_at)
SELECT @scenario, N'Evening arrival pressure', N'Synthetic baseline with one pending general-ward bed request.', '2026-09-12 00:00:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_scenarios WHERE id = @scenario);

INSERT INTO dbo.simulation_scenario_versions (id, scenario_id, version, parameters_json, created_at)
SELECT @scenarioVersion, @scenario, 1, N'{"arrival_rate_per_hour":4,"icu_staffed_beds":1,"general_staffed_beds":2}', '2026-09-12 00:05:00 +00:00'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_scenario_versions WHERE id = @scenarioVersion);

INSERT INTO dbo.simulation_snapshots (id, captured_at, schema_version, state_json)
SELECT @snapshot, '2026-09-12 00:10:00 +00:00', N'1.0', N'{"patients":3,"wards":3,"beds":4,"active_admissions":2,"queued_bed_requests":1}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_snapshots WHERE id = @snapshot);

INSERT INTO dbo.simulation_runs (id, scenario_version_id, snapshot_id, random_seed, engine_version, simulation_start_at, simulation_end_at, started_at, finished_at, status)
SELECT @run, @scenarioVersion, @snapshot, 20260912, N'mock-engine-1.0', '2026-09-12 00:00:00 +00:00', '2026-09-13 00:00:00 +00:00', '2026-09-12 00:15:00 +00:00', '2026-09-12 00:16:30 +00:00', N'completed'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_runs WHERE id = @run);

INSERT INTO dbo.simulation_events (id, run_id, sequence_no, simulated_at, event_type, entity_type, entity_id, payload_json)
SELECT '91000000-0000-0000-0000-000000000001', @run, 1, '2026-09-12 00:00:00 +00:00', N'arrival', N'patient', @pAlan, N'{"queue":"bed","ward":"GEN"}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_events WHERE id = '91000000-0000-0000-0000-000000000001');

INSERT INTO dbo.simulation_events (id, run_id, sequence_no, simulated_at, event_type, entity_type, entity_id, payload_json)
SELECT '91000000-0000-0000-0000-000000000002', @run, 2, '2026-09-12 00:30:00 +00:00', N'capacity_sampled', N'ward', @wGeneral, N'{"effective_capacity":2,"occupied_beds":2,"free_beds":0}'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_events WHERE id = '91000000-0000-0000-0000-000000000002');

INSERT INTO dbo.simulation_metric_samples (id, run_id, simulated_at, ward_id, metric_name, value, unit)
SELECT '92000000-0000-0000-0000-000000000001', @run, '2026-09-12 00:30:00 +00:00', @wGeneral, N'occupancy_percentage', 100.000000, N'percent'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_metric_samples WHERE id = '92000000-0000-0000-0000-000000000001');

INSERT INTO dbo.simulation_metric_samples (id, run_id, simulated_at, ward_id, metric_name, value, unit)
SELECT '92000000-0000-0000-0000-000000000002', @run, '2026-09-12 00:30:00 +00:00', @wGeneral, N'bed_queue_length', 1.000000, N'patients'
WHERE NOT EXISTS (SELECT 1 FROM dbo.simulation_metric_samples WHERE id = '92000000-0000-0000-0000-000000000002');

COMMIT TRANSACTION;
