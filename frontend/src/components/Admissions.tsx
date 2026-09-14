import { useState, type FormEvent } from 'react'
import { dischargeAdmission } from '../lib/admissions'
import { api, fieldErrors } from '../lib/api'
import type { AdmitPatientRequest } from '../lib/contracts'
import { track } from '../lib/telemetry'
import type { Admission, Patient, Ward } from '../lib/types'
import { ErrorAlert } from './ErrorAlert'

type Props = {
  admissions: Admission[]
  patients: Patient[]
  wards: Ward[]
  canWrite: boolean
  onChanged: () => void
}

/** The inputs below carry their own messages, so ErrorAlert must not repeat them. */
const FORM_FIELDS = ['patientId', 'ward']

export function Admissions({ admissions, patients, wards, canWrite, onChanged }: Props) {
  const [patientId, setPatientId] = useState('')
  // The ward is sent as its code: a name is what a person reads, a code is what identifies the
  // ward, and the two can differ between hospitals.
  const [ward, setWard] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  // Keyed by the field name the API was sent, which is the name each input binds to.
  const invalid = fieldErrors(error)

  const messageFor = (name: keyof AdmitPatientRequest) =>
    invalid[name] ? <span className="field-error">{invalid[name].join(' ')}</span> : null

  const nameOf = (id: string) => {
    const p = patients.find((x) => x.id === id)
    return p ? `${p.givenName} ${p.familyName}` : id.slice(0, 8)
  }

  async function run(label: string, fn: () => Promise<Admission>) {
    setBusy(true)
    setError(null)
    try {
      const admission = await fn()
      track(label, { admissionId: admission.id, ward: admission.ward })
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  function admit(e: FormEvent) {
    e.preventDefault()
    const body: AdmitPatientRequest = { patientId, ward }
    run('admission.admitted', () =>
      api<Admission>('/api/admissions', { method: 'POST', body: JSON.stringify(body) }),
    )
  }

  function discharge(admission: Admission) {
    run('admission.discharged', () => dischargeAdmission(admission))
  }

  return (
    <section className="card stack" data-region="admissions">
      <h2>
        Admissions <span className="badge muted">{admissions.filter((a) => a.status === 'Admitted').length} active</span>
      </h2>

      {canWrite && (
        <form className="row" onSubmit={admit}>
          <label>
            Patient
            <select required name="patientId" aria-invalid={invalid.patientId ? true : undefined} value={patientId} onChange={(e) => setPatientId(e.target.value)}>
              <option value="">Select…</option>
              {patients.map((p) => (
                <option key={p.id} value={p.id}>{p.givenName} {p.familyName} ({p.mrn})</option>
              ))}
            </select>
            {messageFor('patientId')}
          </label>
          <label>
            Ward
            <select required name="ward" aria-invalid={invalid.ward ? true : undefined} value={ward} onChange={(e) => setWard(e.target.value)}>
              <option value="">Select…</option>
              {wards.map((w) => (
                // A full ward stays visible but unselectable, so the form shows where there is no
                // room rather than hiding it and leaving the choice unexplained.
                <option key={w.id} value={w.code} disabled={w.freeBeds === 0}>
                  {w.name} {w.freeBeds === 0 ? '(full)' : `(${w.freeBeds} free)`}
                </option>
              ))}
            </select>
            {messageFor('ward')}
          </label>
          <button className="btn" type="submit" data-track="admit" disabled={busy || !patientId || !ward}>Admit</button>
        </form>
      )}
      <ErrorAlert error={error} handled={FORM_FIELDS} />

      {admissions.length === 0 ? (
        <div className="empty">No admissions yet.</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>Patient</th><th>Ward</th><th>Status</th><th></th></tr>
            </thead>
            <tbody>
              {admissions.map((a) => (
                <tr key={a.id}>
                  <td>{nameOf(a.patientId)}</td>
                  <td>{a.ward}</td>
                  <td><span className={`badge ${a.status === 'Admitted' ? 'ok' : 'muted'}`}>{a.status}</span></td>
                  <td>
                    {canWrite && a.status === 'Admitted' && (
                      <button className="btn ghost sm" type="button" data-track="discharge" disabled={busy} onClick={() => discharge(a)}>
                        Discharge
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
