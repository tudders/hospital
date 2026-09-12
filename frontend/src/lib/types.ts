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
