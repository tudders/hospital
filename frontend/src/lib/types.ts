export type Patient = {
  id: string
  mrn: string
  givenName: string
  familyName: string
  dateOfBirth: string
  registeredAt: string
}

export type Admission = {
  id: string
  patientId: string
  ward: string
  status: 'Admitted' | 'Discharged'
  admittedAt: string
  dischargedAt: string | null
}

/**
 * A ward as the admit form needs it. Occupancy counts only - no patient data is carried here.
 * `freeBeds` is what can be allocated now: the lower of usable beds and the staffed limit, less
 * the beds in use, so a ward with empty beds and no staff for them correctly reads as full.
 */
export type Ward = {
  id: string
  code: string
  name: string
  wardType: string
  beds: number
  occupiedBeds: number
  effectiveCapacity: number
  freeBeds: number
}
