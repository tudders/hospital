import { useCallback, useEffect, useState } from 'react'
import { Admissions } from './components/Admissions'
import { ErrorAlert } from './components/ErrorAlert'
import { Login } from './components/Login'
import { Patients } from './components/Patients'
import { Hospital } from './components/Hospital'
import { API_BASE, api } from './lib/api'
import { canWrite, getToken, setToken, type Me } from './lib/auth'
import { startSessionRecording } from './lib/session-recorder'
import { configureTelemetry, sessionId, track } from './lib/telemetry'
import type { Admission, Patient } from './lib/types'

configureTelemetry(API_BASE)

export default function App() {
  const [page, setPage] = useState<'hospital' | 'patients'>('hospital')
  const [me, setMe] = useState<Me | null>(null)
  const [booting, setBooting] = useState(!!getToken())
  const [patients, setPatients] = useState<Patient[]>([])
  const [admissions, setAdmissions] = useState<Admission[]>([])
  const [error, setError] = useState<unknown>(null)

  const refresh = useCallback(async () => {
    try {
      const [p, a] = await Promise.all([api<Patient[]>('/api/patients'), api<Admission[]>('/api/admissions')])
      setPatients(p)
      setAdmissions(a)
      setError(null)
    } catch (err) {
      setError(err)
    }
  }, [])

  const loggedIn = useCallback(
    (who: Me) => {
      setMe(who)
      refresh()
    },
    [refresh],
  )

  // Recording is independent of auth: it starts with the tab and stops with it.
  useEffect(() => startSessionRecording(), [])

  // Restore a session from a stored token.
  useEffect(() => {
    if (!getToken()) return
    api<Me>('/api/auth/me')
      .then(loggedIn)
      .catch(() => setToken(null))
      .finally(() => setBooting(false))
  }, [loggedIn])


  function logout() {
    track('auth.logout')
    setToken(null)
    setMe(null)
    setPatients([])
    setAdmissions([])
    setPage('hospital')
  }

  if (booting) return null
  if (!me) return <div className="app"><Login onLoggedIn={loggedIn} /></div>

  const write = canWrite(me)
  return (
    <div className={`app ${page === 'hospital' ? 'app-hospital' : ''}`}>
      <header className="topbar">
        <h1>Alcidion patient flow</h1>
        <div className="who">
          {me.name} · {me.roles.join(', ') || 'read-only'} · session <code>{sessionId.slice(0, 8)}</code>{' '}
          <button className="btn ghost sm" type="button" data-track="sign-out" onClick={logout}>Sign out</button>
        </div>
      </header>
      <nav className="app-navigation" data-region="navigation" aria-label="Application navigation">
        <button data-track="navigate-hospital" aria-current={page === 'hospital' ? 'page' : undefined} onClick={() => setPage('hospital')}>▥ Hospital overview</button>
        <button data-track="navigate-patients" aria-current={page === 'patients' ? 'page' : undefined} onClick={() => setPage('patients')}>Patients & admissions</button>
      </nav>
      {page === 'hospital' ? <Hospital key={me.name} /> : <>
        <ErrorAlert error={error} />
        <div className="grid">
          <Patients patients={patients} canWrite={write} onChanged={refresh} onViewHospital={() => setPage('hospital')} />
          <Admissions admissions={admissions} patients={patients} canWrite={write} onChanged={refresh} onViewHospital={() => setPage('hospital')} />
        </div>
      </>}
    </div>
  )
}
