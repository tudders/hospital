import { api } from './api'
import type { CorrectPatientRequest } from './contracts'
import type { Patient } from './types'

/**
 * Corrects a patient's demographics, taken against the version the record was read at.
 *
 * `If-Match` is what makes the call safe to repeat, and safe to make at all on a screen two people
 * can have open. Demographics are exactly what gets corrected twice - a clerk fixing an MRN while a
 * nurse fixes the spelling of a name - and without the version the second save writes its own whole
 * record over the first, silently reinstating the value the other person had just fixed. Quoting it
 * means the later save is refused with a 412 instead.
 *
 * Only the properties `correction` carries change; everything left out is left alone, which is what
 * makes two people correcting two different fields a sequence rather than a fight. `changedFields`
 * in `corrections.ts` is what narrows a form to that.
 */
export function correctPatient(patient: Patient, correction: CorrectPatientRequest): Promise<Patient> {
  return api<Patient>(`/api/patients/${patient.id}`, {
    method: 'PATCH',
    headers: { 'If-Match': `"${patient.version}"` },
    body: JSON.stringify(correction),
  })
}
