/* eslint-disable */
// Generated from backend/src/Alcidion.Api.Contracts/Schemas/*.json - do not edit by hand.
// Run `npm run contracts` after changing a schema; `npm run build` fails if this is stale.

/**
 * One recorded frontend event.
 * Open: the API ignores properties not listed here rather than refusing them.
 * Element of ClientEventBatch.
 */
export type ClientEvent = {
  /**
   * Event name, e.g. 'session.start' or 'ui.click'. It becomes a metric dimension, so its shape is
   * bounded.
   * minLength: 1, maxLength: 64, pattern: ^[a-z][a-z0-9]*([._-][a-z0-9]+)*$
   */
  name: string
  /**
   * One id per browser tab. A crypto.randomUUID() today, but not required to be one - tabs carry
   * ids issued before that was true.
   * minLength: 1, maxLength: 64, pattern: \S
   */
  sessionId: string
  /**
   * Wall-clock time the event was recorded, from the browser's clock.
   * format: date-time
   */
  at: string
  /**
   * Monotonic per session. Orders the session regardless of the order batches arrive in.
   * minimum: 0
   */
  seq?: number
  /**
   * Milliseconds since session start, so a session can be replayed at the pace the user
   * experienced.
   * minimum: 0
   */
  t?: number
  /**
   * Event payload. Deliberately open: capture in session-recorder.ts adds fields per event type,
   * and telemetry must never 400 a client that knows about one this version does not.
   */
  props?: Record<string, unknown>
}

/**
 * Body of POST /api/telemetry/events: one flush of the browser's event buffer, oldest first.
 * minItems: 1, maxItems: 500
 * Generated from client-event-batch.json.
 */
export type ClientEventBatch = ClientEvent[]

/**
 * Body of POST /api/patients. Shape, length and format only - meaning stays with the Patient
 * aggregate.
 * Open: the API ignores properties not listed here rather than refusing them.
 * Generated from register-patient-request.json.
 */
export type RegisterPatientRequest = {
  /**
   * Medical Record Number, unique per facility. Surrounding whitespace is tolerated deliberately:
   * the aggregate trims and upper-cases, so a padded MRN must reach it and come back 409 rather
   * than 400. The pattern and maxLength agree at 64 characters on purpose - a frontend generated
   * from this document sets maxlength=64 on the input, and a shorter pattern would 400 a value the
   * input let the user type.
   * minLength: 1, maxLength: 64, pattern: ^\s*[A-Za-z0-9][A-Za-z0-9._-]{0,63}\s*$
   */
  mrn: string
  /**
   * minLength: 1, maxLength: 100, pattern: \S
   */
  givenName: string
  /**
   * minLength: 1, maxLength: 100, pattern: \S
   */
  familyName: string
  /**
   * The pattern carries the 1875 floor, which JSON Schema cannot express as a date comparison.
   * There is deliberately no upper bound here: 'not in the future' is compared against the
   * injected clock in Patient.Register, and a static schema would bake in a build-time date.
   * pattern: ^(18(7[5-9]|[89][0-9])|19[0-9]{2}|[2-9][0-9]{3})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$, format: date
   */
  dateOfBirth: string
}
