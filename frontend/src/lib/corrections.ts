/**
 * What a correction form actually asks the API to change.
 *
 * A form round-trips every field it shows, so an untouched form would send all four and claim to
 * correct things nobody looked at. `PATCH` means "only what is named here", and the whole value of
 * that is lost if the client names everything: two people correcting two different fields become a
 * fight over the whole record rather than a sequence.
 *
 * Pure on purpose, and separate from `patients.ts` for the same reason `redaction.ts` is separate
 * from `telemetry.ts`: `npm test` runs these modules through `node --test` directly, which resolves
 * no bundler paths, so the logic worth testing lives where it can be imported on its own.
 */

import type { CorrectPatientRequest } from './contracts.ts'
import type { Patient } from './types.ts'

/**
 * The fields of `correction` that differ from what `patient` already says.
 *
 * Comparison is against the normalised form the aggregate stores - MRNs trimmed and upper-cased,
 * names trimmed - so retyping a value exactly as it already is counts as no change. What is *sent*
 * is what the user typed: trimming on the way out would quietly alter the request, and the schema
 * tolerates the padding precisely so the aggregate can be the one to trim it.
 */
export function changedFields(patient: Patient, correction: CorrectPatientRequest): CorrectPatientRequest {
  const changed: CorrectPatientRequest = {}
  if (correction.mrn !== undefined && correction.mrn.trim().toUpperCase() !== patient.mrn) changed.mrn = correction.mrn
  if (correction.givenName !== undefined && correction.givenName.trim() !== patient.givenName) changed.givenName = correction.givenName
  if (correction.familyName !== undefined && correction.familyName.trim() !== patient.familyName) changed.familyName = correction.familyName
  if (correction.dateOfBirth !== undefined && correction.dateOfBirth !== patient.dateOfBirth) changed.dateOfBirth = correction.dateOfBirth
  return changed
}
