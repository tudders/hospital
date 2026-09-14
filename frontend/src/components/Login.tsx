import { useState, type FormEvent } from 'react'
import { api, fieldErrors } from '../lib/api'
import { setToken, type Me } from '../lib/auth'
import type { LoginRequest } from '../lib/contracts'
import { track } from '../lib/telemetry'
import { ErrorAlert } from './ErrorAlert'

const DEMO: LoginRequest = { username: 'nurse', password: 'nurse' }

/** The inputs below carry their own messages, so ErrorAlert must not repeat them. */
const FORM_FIELDS = Object.keys(DEMO)

export function Login({ onLoggedIn }: { onLoggedIn: (me: Me) => void }) {
  const [form, setForm] = useState<LoginRequest>(DEMO)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  // Keyed by the field name the API was sent. A wrong password is a 401 and names no field, so
  // this stays empty for it and ErrorAlert shows "Invalid credentials" on its own.
  const invalid = fieldErrors(error)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const { accessToken } = await api<{ accessToken: string }>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify(form),
      })
      setToken(accessToken)
      const me = await api<Me>('/api/auth/me')
      // Joined, not the array: props carries flat scalars only, and an array here cost the whole
      // login batch - nine events, every session - a 400 the transport swallows.
      track('auth.login', { user: me.name, roles: me.roles.join(' ') })
      onLoggedIn(me)
    } catch (err) {
      setToken(null)
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  function field(name: keyof LoginRequest) {
    return {
      name,
      value: form[name],
      'aria-invalid': invalid[name] ? true : undefined,
      onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [name]: e.target.value }),
    }
  }

  function message(name: keyof LoginRequest) {
    const messages = invalid[name]
    return messages ? <span className="field-error">{messages.join(' ')}</span> : null
  }

  return (
    <form className="card login stack" data-region="login" onSubmit={submit}>
      <h1>Sign in</h1>
      <label>
        Username
        {/* maxLength matches the schema's, so the input cannot produce a value that 400s. */}
        <input required maxLength={64} autoComplete="username" {...field('username')} />
        {message('username')}
      </label>
      <label>
        Password
        <input required type="password" maxLength={256} autoComplete="current-password" {...field('password')} />
        {message('password')}
      </label>
      <ErrorAlert error={error} handled={FORM_FIELDS} />
      <button className="btn" type="submit" disabled={busy}>
        {busy ? 'Signing in…' : 'Sign in'}
      </button>
      <p className="hint">
        Demo users: nurse/nurse, doctor/doctor (clinician), admin/admin, viewer/viewer (read-only).
      </p>
    </form>
  )
}
