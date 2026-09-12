import { useState, type FormEvent } from 'react'
import { api } from '../lib/api'
import { track } from '../lib/telemetry'
import type { Patient } from '../lib/types'
import { ErrorAlert } from './ErrorAlert'

type Props = {
  patients: Patient[]
  canWrite: boolean
  onChanged: () => void
}

export function Patients({ patients, canWrite, onChanged }: Props) {
  const [form, setForm] = useState({ mrn: '', givenName: '', familyName: '', dateOfBirth: '' })
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function register(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const p = await api<Patient>('/api/patients', { method: 'POST', body: JSON.stringify(form) })
      track('patient.registered', { patientId: p.id })
      setForm({ mrn: '', givenName: '', familyName: '', dateOfBirth: '' })
      onChanged()
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card stack" data-region="patients">
      <h2>
        Patients <span className="badge muted">{patients.length}</span>
      </h2>

      {canWrite && (
        <form className="row" onSubmit={register}>
          <label>
            MRN
            <input required name="mrn" value={form.mrn} onChange={(e) => setForm({ ...form, mrn: e.target.value })} />
          </label>
          <label>
            Given name
            <input required name="givenName" value={form.givenName} onChange={(e) => setForm({ ...form, givenName: e.target.value })} />
          </label>
          <label>
            Family name
            <input required name="familyName" value={form.familyName} onChange={(e) => setForm({ ...form, familyName: e.target.value })} />
          </label>
          <label>
            Date of birth
            <input required type="date" name="dateOfBirth" value={form.dateOfBirth} onChange={(e) => setForm({ ...form, dateOfBirth: e.target.value })} />
          </label>
          <button className="btn" type="submit" data-track="register" disabled={busy}>Register</button>
        </form>
      )}
      <ErrorAlert error={error} />

      {patients.length === 0 ? (
        <div className="empty">No patients yet.</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr><th>MRN</th><th>Name</th><th>DOB</th></tr>
            </thead>
            <tbody>
              {patients.map((p) => (
                <tr key={p.id}>
                  <td><code>{p.mrn}</code></td>
                  <td>{p.givenName} {p.familyName}</td>
                  <td>{p.dateOfBirth}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
