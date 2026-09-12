import { useState, type FormEvent } from 'react'
import { api } from '../lib/api'
import { track } from '../lib/telemetry'
import type { Admission, Patient } from '../lib/types'
import { ErrorAlert } from './ErrorAlert'

type Props = {
  admissions: Admission[]
  patients: Patient[]
  canWrite: boolean
  onChanged: () => void
}

const WARDS = ['ED', 'ICU', 'Ward 3B', 'Maternity', 'Paediatrics']

export function Admissions({ admissions, patients, canWrite, onChanged }: Props) {
  const [patientId, setPatientId] = useState('')
  const [ward, setWard] = useState(WARDS[0])
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

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
    run('admission.admitted', () =>
      api<Admission>('/api/admissions', { method: 'POST', body: JSON.stringify({ patientId, ward }) }),
    )
  }

  function discharge(id: string) {
    run('admission.discharged', () => api<Admission>(`/api/admissions/${id}/discharge`, { method: 'POST' }))
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
            <select required name="patientId" value={patientId} onChange={(e) => setPatientId(e.target.value)}>
              <option value="">Select…</option>
              {patients.map((p) => (
                <option key={p.id} value={p.id}>{p.givenName} {p.familyName} ({p.mrn})</option>
              ))}
            </select>
          </label>
          <label>
            Ward
            <select name="ward" value={ward} onChange={(e) => setWard(e.target.value)}>
              {WARDS.map((w) => <option key={w}>{w}</option>)}
            </select>
          </label>
          <button className="btn" type="submit" data-track="admit" disabled={busy || !patientId}>Admit</button>
        </form>
      )}
      <ErrorAlert error={error} />

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
                      <button className="btn ghost sm" type="button" data-track="discharge" disabled={busy} onClick={() => discharge(a.id)}>
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
