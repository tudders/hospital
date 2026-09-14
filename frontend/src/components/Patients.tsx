import { useState, type FormEvent } from 'react'
import { api, fieldErrors } from '../lib/api'
import type { RegisterPatientRequest } from '../lib/contracts'
import { track } from '../lib/telemetry'
import type { Patient } from '../lib/types'
import { ErrorAlert } from './ErrorAlert'
import { useToast } from './toast-context'

type Props = {
  patients: Patient[]
  canWrite: boolean
  onChanged: () => void
}

const EMPTY: RegisterPatientRequest = { mrn: '', givenName: '', familyName: '', dateOfBirth: '' }

/** The inputs below carry their own messages, so ErrorAlert must not repeat them. */
const FORM_FIELDS = Object.keys(EMPTY)

export function Patients({ patients, canWrite, onChanged }: Props) {
  const { showToast } = useToast()
  const [form, setForm] = useState<RegisterPatientRequest>(EMPTY)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  // Keyed by the field name the API was sent, which is the name each input binds to.
  const invalid = fieldErrors(error)

  async function register(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const p = await api<Patient>('/api/patients', { method: 'POST', body: JSON.stringify(form) })
      track('patient.registered', { patientId: p.id })
      setForm(EMPTY)
      onChanged()
      showToast('Patient details added successfully')
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  function field(name: keyof RegisterPatientRequest) {
    return {
      name,
      value: form[name],
      'aria-invalid': invalid[name] ? true : undefined,
      onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [name]: e.target.value }),
    }
  }

  function message(name: keyof RegisterPatientRequest) {
    const messages = invalid[name]
    return messages ? <span className="field-error">{messages.join(' ')}</span> : null
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
            {/* maxLength matches the schema's, so the input cannot produce a value that 400s. */}
            <input required maxLength={64} {...field('mrn')} />
            {message('mrn')}
          </label>
          <label>
            Given name
            <input required maxLength={100} {...field('givenName')} />
            {message('givenName')}
          </label>
          <label>
            Family name
            <input required maxLength={100} {...field('familyName')} />
            {message('familyName')}
          </label>
          <label>
            Date of birth
            <input required type="date" {...field('dateOfBirth')} />
            {message('dateOfBirth')}
          </label>
          <button className="btn" type="submit" data-track="register" disabled={busy}>Register</button>
        </form>
      )}
      <ErrorAlert error={error} handled={FORM_FIELDS} />

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
