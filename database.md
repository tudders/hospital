# Hospital database notes

Design for a synthetic hospital: register patients, record conditions, admit them, queue and deliver treatments, move them between ward beds, and discharge them. Preserve the timing of each step so we can monitor patient flow and capacity, then compare simulated scenarios.

The tracked SQL migrations implement one hospital and synthetic patients. The existing Patients and Admissions domains remain the starting point; the physical hierarchy, staffing and treatment scheduling extend them.

## Conventions

- `id` is a UUID primary key. Fields ending in `_id` are foreign keys unless described otherwise.
- Timestamps are UTC instants. Intervals use `[start, end)`: a bed released at 10:00 can be occupied again at 10:00.
- A nullable actual end timestamp means an activity is still open. Keep expected end times separate from actual end times.
- Store date of birth and derive age at the observation or simulation time. Do not store an age that becomes stale.
- Store weight in kilograms as a decimal and keep measurement history.
- Derive durations from timestamps; store planned durations in minutes. These are synthetic model inputs, not clinical recommendations.
- Mutable flow records carry a concurrency version so updates can reject stale writes.

## Tables to create first

| Table | Main columns | Purpose |
|---|---|---|
| `hospitals` | `id`, `code`, `name` | Hospital identity. The full mock fixture seeds one hospital. |
| `floors` | `id`, `hospital_id`, `floor_number`, `code`, `name` | Physical floors within a hospital. |
| `patients` | `id`, `mrn`, `given_name`, `family_name`, `date_of_birth`, `gender`, `registered_at` | Patient identity. Gender supports unknown/not specified. MRN is normalized and unique. |
| `patient_weight_measurements` | `id`, `patient_id`, `weight_kg`, `measured_at` | Weight history; latest measurement at a given time supplies the patient's weight. |
| `condition_types` | `id`, `code`, `name`, `description` | Catalogue of synthetic conditions. |
| `patient_conditions` | `id`, `patient_id`, `condition_type_id`, `onset_at`, `recorded_at`, `expected_resolved_at`, `resolved_at`, `severity` | A condition episode. Actual duration is onset to resolution, or elapsed time for an ongoing condition. Recurrences get separate rows. |
| `wards` | `id`, `floor_id`, `code`, `name`, `ward_type` | Ward identity, such as ED, ICU or a general ward. Capacity comes from beds and staffing intervals below. |
| `rooms` | `id`, `floor_id`, `ward_id`, `room_number`, `code`, `name` | Six rooms per ward in the full mock fixture. |
| `beds` | `id`, `room_id`, `ward_id`, `bed_number`, `code`, `bed_type`, `available_from`, `retired_at` | Physical bed inventory. The full mock fixture has six beds per room. |
| `staff_members` | `id`, `hospital_id`, `employee_number`, `given_name`, `family_name`, `role`, `specialty` | Synthetic hospital staff. |
| `staff_ward_assignments` | `staff_id`, `ward_id`, `shift_code`, `starts_at`, `ends_at` | Shift-level ward staffing. |
| `patient_care_assignments` | `admission_id`, `staff_id`, `assignment_role`, `starts_at`, `ends_at` | Patient-linked care teams for active admissions. |
| `ward_capacity_periods` | `id`, `ward_id`, `starts_at`, `ends_at`, `staffed_bed_limit` | Time-varying staffing limit. A ward with 20 physical beds may only staff 15. |
| `bed_blocks` | `id`, `bed_id`, `starts_at`, `ends_at`, `reason` | Periods when a bed cannot accept patients: cleaning, maintenance or closure. |
| `treatment_types` | `id`, `code`, `name`, `description`, `default_duration_minutes`, `requires_bed`, `required_bed_type` | Catalogue of treatments and their initial planned durations. |
| `ward_treatment_capabilities` | `id`, `ward_id`, `treatment_type_id`, `starts_at`, `ends_at`, `max_concurrent`, `duration_override_minutes` | Which treatments each ward can deliver, when, and how many at once. A treatment can be available in several wards. |
| `admissions` | `id`, `patient_id`, `requested_at`, `admitted_at`, `expected_discharge_at`, `discharged_at`, `cancelled_at`, `priority` | One hospital episode. Supports waiting before admission, inpatient stay, discharge and readmission history. Ward location is recorded in bed stays, not overwritten here. |
| `treatment_orders` | `id`, `admission_id`, `treatment_type_id`, `ordered_at`, `ready_at`, `planned_duration_minutes`, `priority`, `cancelled_at` | A requested treatment, including when it becomes eligible to start. Several orders may run sequentially or concurrently during an admission. |
| `treatment_order_conditions` | `treatment_order_id`, `patient_condition_id` | Many-to-many link between treatments and the condition episodes they address. Composite primary key. |
| `treatment_order_dependencies` | `treatment_order_id`, `prerequisite_order_id` | Optional ordering rules: treatment B cannot start until treatment A completes. Composite primary key. |
| `treatment_sessions` | `id`, `treatment_order_id`, `ward_id`, `bed_stay_id` (nullable), `started_at`, `expected_end_at`, `ended_at`, `outcome` | Actual treatment delivery. Multiple sessions allow pauses or interruptions; outcomes include completed and interrupted. Records the delivering ward explicitly. |
| `bed_requests` | `id`, `admission_id`, `treatment_order_id` (nullable), `target_ward_id` (nullable), `required_bed_type`, `requested_at`, `priority`, `fulfilled_at`, `cancelled_at` | Queue for initial placement or transfer. A null target ward allows allocation to any compatible ward. |
| `bed_stays` | `id`, `admission_id`, `bed_id`, `bed_request_id` (nullable), `started_at`, `expected_end_at`, `ended_at`, `end_reason` | Actual occupancy history. A transfer ends one stay and starts another; discharge releases the final bed. |
| `flow_events` | `id`, `admission_id`, `event_type`, `occurred_at`, `recorded_at`, `correlation_id`, `payload_json` | Append-only audit timeline: requested, admitted, queued, allocated, treatment started/completed, transferred and discharged. Typed tables remain the source of operational state. |

## Relationships and rules

- A patient has many weights, condition episodes and admissions. Initially allow only one open, non-cancelled admission per patient; a discharged patient can be readmitted.
- An admission has many treatment orders, bed requests and bed stays. A treatment-condition link must refer to the same patient as its admission.
- A ward has many beds and treatment capabilities. A treatment session must use an active capability for its ward and treatment type.
- If a session requires a bed, its bed stay must belong to the same admission and delivering ward, and cover the session interval. Bed-free treatments can omit it.
- Treatment dependencies stay within one admission and cannot form cycles. All prerequisites must complete before a dependent order starts.
- A treatment order is complete when a session completes it. An interrupted session can be followed by another session; a cancelled order cannot start another session.
- Persist the planned duration on each order so changing catalogue defaults does not rewrite historical expectations.
- No overlapping occupancy intervals for the same bed or the same admission. Concurrent treatment sessions for a patient are allowed only where the scenario rules permit them.
- No overlapping staffing periods for a ward, or capability periods for the same ward/treatment pair. An uncovered staffing period means zero allocatable capacity; seed an open-ended period for normal operation.
- Allocation must check bed blocks, bed compatibility, staffed capacity and treatment capacity together. Enforce allocation and uniqueness atomically; a check followed by an independent insert is insufficient.
- New blocks cannot overlap occupied intervals. A staffing reduction below current occupancy records an over-capacity state and prevents new allocations until capacity is available.
- Transfers atomically close the previous stay and open the next. Discharge closes occupancy and cancels pending requests/orders; active sessions must first be ended explicitly.
- Closing a bed stay can create a cleaning block. Occupancy ending does not necessarily mean the bed is immediately available.
- End times cannot precede start times. Weights must be positive; capacities are non-negative integers; planned treatment durations are positive.

Use unique constraints for normalized MRNs and catalogue codes, foreign keys for relationships, and database transactions with appropriate locking for allocation. Where the database cannot express interval overlap constraints directly, enforce them inside the allocation transaction.

## Monitoring queries / views

Calculate these at time `t` rather than storing counters that can drift:

| View / metric | Definition |
|---|---|
| Patient board | Name, age at `t`, latest weight at or before `t`, gender, active conditions, current treatment, current ward/bed and elapsed stay. |
| Ward capacity | Physical beds existing at `t`, blocked beds, usable beds, staffed limit, occupied beds and queued requests. |
| Effective bed capacity | Minimum of usable physical beds and the staffed bed limit. |
| Free allocatable beds | `max(0, effective capacity - occupied beds)`, then filter candidate beds for the request's bed type and ward/treatment compatibility. |
| Occupancy percentage | Occupied beds divided by effective capacity. Report zero capacity separately; preserve values above 100% after staffing reductions. |
| Treatment capacity | Active sessions versus `max_concurrent` for each ward/treatment capability. Free beds alone do not imply treatment capacity. |
| Bed wait | Request to fulfillment; for pending requests, request to `t`. Separate cancelled requests. |
| Treatment wait | `ready_at` to first session start, or to `t` while waiting. |
| Treatment duration | Sum of session intervals; compare with the order's planned duration. |
| Condition duration | `onset_at` to `resolved_at`, or to `t` while active. |
| Length of stay | `admitted_at` to `discharged_at`, or to `t` for admitted patients. |
| Flow / throughput | Admissions, transfers and discharges per hour/day; include queue lengths and median / 95th percentile waits. |

Index admission history by patient, weight measurements by patient/time, occupancy by bed/time and admission/time, pending requests by priority/request time, and events by admission/time and correlation ID. For occupancy over a period, use occupied bed-minutes divided by effective capacity-minutes, not an average of percentages.

## Tables for later scenario simulation

Keep simulation state separate from operational tables. A simulation must not consume real bed capacity or alter the starting dataset.

| Table | Main columns | Purpose |
|---|---|---|
| `simulation_scenarios` | `id`, `name`, `description`, `created_at` | Named experiment, such as increased arrivals or an ICU closure. |
| `simulation_scenario_versions` | `id`, `scenario_id`, `version`, `parameters_json`, `created_at` | Immutable parameters: arrival distributions, patient/condition mix, treatment pathways and duration distributions, transfer rules, cleaning times and ward/staffing changes. Validate and version the JSON schema. |
| `simulation_snapshots` | `id`, `captured_at`, `schema_version`, `state_json` | Immutable initial state containing synthetic patients, conditions, weights, catalogues, capabilities, capacity, occupancy, queues and in-progress work. |
| `simulation_runs` | `id`, `scenario_version_id`, `snapshot_id`, `random_seed`, `engine_version`, `simulation_start_at`, `simulation_end_at`, `started_at`, `finished_at`, `status` | One execution. Fixed inputs, engine version and seed make results reproducible. Wall-clock execution times are separate from simulated times. |
| `simulation_events` | `id`, `run_id`, `sequence_no`, `simulated_at`, `event_type`, `entity_type`, `entity_id`, `payload_json` | Ordered simulated arrivals, allocations, treatment completions, transfers and departures. Entity IDs refer to run-local entities, not operational foreign keys. `(run_id, sequence_no)` is unique and breaks timestamp ties. |
| `simulation_metric_samples` | `id`, `run_id`, `simulated_at`, `ward_id` (nullable snapshot reference), `metric_name`, `value`, `unit` | Capacity, occupancy, queues and throughput samples for charts and comparisons. |

Reconstruct run state from the snapshot plus ordered events initially; add run-scoped projection tables only if query performance requires them. Use the existing injectable clock for simulated time, and an injectable seeded random source for durations and arrivals. Repeat each scenario with several seeds to compare variability as well as averages.

## First end-to-end example

1. Register a synthetic patient with date of birth and gender; record weight and a condition episode.
2. Create an admission request and a treatment order linked to the condition, with a planned duration.
3. Queue a bed request for a compatible ward. If no staffed compatible bed is available, the patient remains waiting.
4. Allocate a bed, start the bed stay and mark the patient admitted. Start treatment when its prerequisites and the ward's treatment capacity permit it.
5. Complete treatment after its duration. Resolve the condition separately if the model calls for it; treatment completion need not imply condition resolution or discharge.
6. Transfer for further treatment or discharge. Close the bed stay and block the released bed for cleaning.
7. Monitor the resulting occupancy, wait times and throughput. Later rerun the same initial state with changed arrivals, beds, staffing or treatment durations.

## Suggested implementation order

1. Patients, weight/condition history, wards, beds, staffing periods and admissions.
2. Bed requests, stays, blocks and transactional allocation; capacity and patient-board views.
3. Treatment catalogues, ward capabilities, orders, sessions and dependencies.
4. Flow events correlated with the existing frontend/backend telemetry.
5. Immutable scenario inputs, simulation engine, run events and comparison charts.

Carry forward the existing `PatientRegistered` event into the Admissions read model. Add events for allocation, transfer, treatment completion and discharge. If database writes and events must survive failures together, introduce an outbox when persistence is implemented.
