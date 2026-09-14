import { api, ApiError } from './api'
import type { ChangeAdmissionRequest } from './contracts'
import type { Admission } from './types'

/**
 * One transition, named the way the schema names it. The generated type has both properties
 * optional because JSON Schema counts them rather than pairing them; this union is the same rule
 * said in TypeScript, so a body carrying both cannot be written here in the first place.
 */
export type AdmissionChange = { ward: string } | { status: 'discharged' }

/**
 * Changes an admission, taken against the version it was read at.
 *
 * `If-Match` is what makes the call safe to repeat. Both transitions rewrite where a patient is,
 * and both are reachable twice - a retry after a dropped response, a second clinician acting on
 * the same board. Quoting the version means the later arrival is refused with a 412 instead of
 * moving the patient again, closing the stay the first one opened and claiming a second bed.
 */
export function changeAdmission(admission: Admission, change: AdmissionChange): Promise<Admission> {
  const body: ChangeAdmissionRequest = change
  return api<Admission>(`/api/admissions/${admission.id}`, {
    method: 'PATCH',
    headers: { 'If-Match': `"${admission.version}"` },
    body: JSON.stringify(body),
  })
}

export const dischargeAdmission = (admission: Admission) => changeAdmission(admission, { status: 'discharged' })

export const transferAdmission = (admission: Admission, ward: string) => changeAdmission(admission, { ward })

/**
 * True when the change was refused because the admission had already moved on. Worth separating
 * from other failures: nothing about the request was wrong, so the screen owes the user fresh data
 * rather than an apology, and refreshing is what makes a retry meaningful.
 */
export const isStale = (error: unknown) => error instanceof ApiError && error.problem.status === 412
