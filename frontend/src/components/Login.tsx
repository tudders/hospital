import { useState, type FormEvent } from 'react'
import { api } from '../lib/api'
import { setToken, type Me } from '../lib/auth'
import { track } from '../lib/telemetry'
import { ErrorAlert } from './ErrorAlert'

export function Login({ onLoggedIn }: { onLoggedIn: (me: Me) => void }) {
  const [username, setUsername] = useState('nurse')
  const [password, setPassword] = useState('nurse')
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const { accessToken } = await api<{ accessToken: string }>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ username, password }),
      })
      setToken(accessToken)
      const me = await api<Me>('/api/auth/me')
      track('auth.login', { user: me.name, roles: me.roles })
      onLoggedIn(me)
    } catch (err) {
      setToken(null)
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card login stack" data-region="login" onSubmit={submit}>
      <h1>Sign in</h1>
      <label>
        Username
        <input name="username" value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" />
      </label>
      <label>
        Password
        <input type="password" name="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
      </label>
      <ErrorAlert error={error} />
      <button className="btn" type="submit" disabled={busy}>
        {busy ? 'Signing in…' : 'Sign in'}
      </button>
      <p className="hint">
        Demo users: nurse/nurse, doctor/doctor (clinician), admin/admin, viewer/viewer (read-only).
      </p>
    </form>
  )
}
