import { useCallback, useEffect, useState } from 'react'
import { PatientFlow } from './components/PatientFlow'
import { ErrorAlert } from './components/ErrorAlert'
import { Login } from './components/Login'
import { Hospital } from './components/Hospital'
import { Layout } from './components/Layout'
import { API_BASE, api } from './lib/api'
import { canWrite, getToken, setToken, type Me } from './lib/auth'
import { startSessionRecording } from './lib/session-recorder'
import { configureTelemetry, track } from './lib/telemetry'
import type { Admission, Ward } from './lib/types'

configureTelemetry(API_BASE)

export default function App() {
  const [page, setPage] = useState<'hospital' | 'patients'>('hospital')
  const [me, setMe] = useState<Me | null>(null)
  const [booting, setBooting] = useState(!!getToken())
  const [admissions, setAdmissions] = useState<Admission[]>([])
  const [wards, setWards] = useState<Ward[]>([])
  const [locatePatientId, setLocatePatientId] = useState<string | null>(null)
  const [error, setError] = useState<unknown>(null)
  const clearLocate = useCallback(() => setLocatePatientId(null), [])

  const refresh = useCallback(async () => {
    try {
      // One round trip for the whole page: the ward list carries live free-bed counts, so it is
      // refreshed with the admissions it constrains rather than fetched once and left to go stale.
      const [a, w] = await Promise.all([
        api<Admission[]>('/api/admissions'),
        api<Ward[]>('/api/wards'),
      ])
      setAdmissions(a)
      setWards(w)
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
    setAdmissions([])
    setWards([])
    clearLocate()
    setPage('hospital')
  }

  if (booting) return null
  if (!me) return <Login onLoggedIn={loggedIn} />

  const write = canWrite(me)
  return (
    <Layout me={me} currentPage={page} onNavigate={next => { clearLocate(); setPage(next) }} onLogout={logout}>
      <div className={page === 'hospital' ? 'app-hospital' : ''}>
        {page === 'hospital' ? <Hospital key={me.name} locatePatientId={locatePatientId} onLocated={clearLocate} /> : <>
          <ErrorAlert error={error} />
          <PatientFlow admissions={admissions} wards={wards} canWrite={write} onChanged={refresh} onLocate={(patientId) => { setLocatePatientId(patientId); setPage('hospital') }} />
        </>}
      </div>
    </Layout>
  )
}
