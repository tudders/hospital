import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { dischargeAdmission, isStale, transferAdmission } from '../lib/admissions'
import { api } from '../lib/api'
import type { RegisterPatientRequest } from '../lib/contracts'
import { track } from '../lib/telemetry'
import type { Admission, Patient, Ward } from '../lib/types'
import { ErrorAlert } from './ErrorAlert'
import { useToast } from './toast-context'

type Props = {
  admissions: Admission[]
  wards: Ward[]
  canWrite: boolean
  onChanged: () => void
  onLocate: (patientId: string) => void
}

type NewAdmissionForm = RegisterPatientRequest & { ward: string }

const EMPTY_ADMIT: NewAdmissionForm = { mrn: '', givenName: '', familyName: '', dateOfBirth: '', ward: '' }
const ADMIT_FIELDS = ['mrn', 'givenName', 'familyName', 'dateOfBirth', 'ward']

const initials = (patient: Patient) => `${patient.givenName[0] ?? ''}${patient.familyName[0] ?? ''}`.toUpperCase()
const fullName = (patient: Patient) => `${patient.givenName} ${patient.familyName}`
const date = (value: string) => new Date(value).toLocaleDateString(undefined, { dateStyle: 'medium' })

export function PatientFlow({ admissions, wards, canWrite, onChanged, onLocate }: Props) {
  const { showToast } = useToast()
  const [tab, setTab] = useState<'admin' | 'reports'>('admin')
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<Patient[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<unknown>(null)
  const [selected, setSelected] = useState<Patient | null>(null)
  const [admit, setAdmit] = useState<NewAdmissionForm>(EMPTY_ADMIT)
  const [admitError, setAdmitError] = useState<unknown>(null)
  const [actionError, setActionError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [transferOpen, setTransferOpen] = useState(false)
  const [destination, setDestination] = useState('')

  useEffect(() => {
    const wanted = query.trim()
    if (!wanted) return

    const controller = new AbortController()
    const timer = setTimeout(async () => {
      setSearching(true)
      setSearchError(null)
      try {
        const next = await api<Patient[]>(`/api/patients?search=${encodeURIComponent(wanted)}`, { signal: controller.signal })
        if (!controller.signal.aborted) setResults(next)
      } catch (err) {
        if (!controller.signal.aborted) setSearchError(err)
      } finally {
        if (!controller.signal.aborted) setSearching(false)
      }
    }, 280)
    return () => { controller.abort(); clearTimeout(timer) }
  }, [query])

  useEffect(() => {
    if (!selected) return
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') setSelected(null) }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [selected])

  const activeAdmission = selected ? admissions.find(a => a.patientId === selected.id && a.status === 'Admitted') : undefined
  const activeCount = admissions.filter(a => a.status === 'Admitted').length

  function changeQuery(next: string) {
    setQuery(next)
    setResults([])
    setSearchError(null)
    setSearching(!!next.trim())
  }

  async function submitAdmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setAdmitError(null)
    try {
      const patient = await api<Patient>('/api/patients', {
        method: 'POST',
        body: JSON.stringify({
          mrn: admit.mrn,
          givenName: admit.givenName,
          familyName: admit.familyName,
          dateOfBirth: admit.dateOfBirth,
        }),
      })
      track('patient.registered', { patientId: patient.id })

      const admission = await api<Admission>('/api/admissions', {
        method: 'POST',
        body: JSON.stringify({ patientId: patient.id, ward: admit.ward }),
      })
      track('admission.admitted', { admissionId: admission.id, ward: admission.ward })
      setAdmit(EMPTY_ADMIT)
      onChanged()
      showToast('Patient details added successfully')
    } catch (err) {
      setAdmitError(err)
    } finally {
      setBusy(false)
    }
  }

  async function discharge() {
    if (!activeAdmission) return
    setBusy(true)
    setActionError(null)
    try {
      await dischargeAdmission(activeAdmission)
      track('admission.discharged', { admissionId: activeAdmission.id })
      onChanged()
      setSelected(null)
    } catch (err) {
      setActionError(err)
      // Refused because the admission moved on: pull the change in so the panel shows what it
      // moved to, and a second press is decided against that rather than against what it was.
      if (isStale(err)) onChanged()
    } finally {
      setBusy(false)
    }
  }

  async function transfer(event: FormEvent) {
    event.preventDefault()
    if (!activeAdmission || !destination) return
    setBusy(true)
    setActionError(null)
    try {
      const moved = await transferAdmission(activeAdmission, destination)
      track('admission.transferred', { admissionId: moved.id, ward: moved.ward })
      onChanged()
      setTransferOpen(false)
      setSelected(null)
    } catch (err) {
      setActionError(err)
      if (isStale(err)) onChanged()
    } finally {
      setBusy(false)
    }
  }

  const reports = useMemo(() => {
    const wardCounts = new Map<string, number>()
    for (const admission of admissions.filter(a => a.status === 'Admitted')) {
      wardCounts.set(admission.ward, (wardCounts.get(admission.ward) ?? 0) + 1)
    }
    const completed = admissions.filter(a => a.dischargedAt)
    const averageStay = completed.length
      ? completed.reduce((total, admission) => total + (Date.parse(admission.dischargedAt!) - Date.parse(admission.admittedAt)), 0) / completed.length / 86_400_000
      : null
    return { wardCounts: [...wardCounts.entries()].sort((a, b) => b[1] - a[1]), completed, averageStay }
  }, [admissions])

  return <section className="patient-flow" data-region="patient-flow">
    <div className="patient-flow-heading">
      <div>
        <p className="eyebrow">PATIENT FLOW / OPERATIONS</p>
        <h2>Patient flow<span className="title-dot">.</span></h2>
        <p className="hint">Find a patient, manage their stay, and keep a clear view of admissions.</p>
      </div>
      <div className="flow-status"><span className="status-dot" />{activeCount} active admissions</div>
    </div>

    <nav className="flow-tabs" aria-label="Patient flow sections">
      <button type="button" className={tab === 'admin' ? 'active' : ''} aria-current={tab === 'admin' ? 'page' : undefined} onClick={() => setTab('admin')}>Admin</button>
      <button type="button" className={tab === 'reports' ? 'active' : ''} aria-current={tab === 'reports' ? 'page' : undefined} onClick={() => setTab('reports')}>Reports</button>
    </nav>

    {tab === 'admin' ? <div className="patient-admin-grid">
      <section className="card patient-panel search-panel">
        <div className="panel-heading"><div><p className="panel-kicker">PATIENT DIRECTORY</p><h3>Search patients</h3></div><span className="panel-icon" aria-hidden="true">⌕</span></div>
        <label className="search-field">Search by MRN or patient name
          <span className="search-input-wrap"><span aria-hidden="true">⌕</span><input type="search" value={query} placeholder="MRN, given name or family name" onChange={event => changeQuery(event.target.value)} /></span>
        </label>
        <div className="search-results" aria-live="polite" aria-busy={searching}>
          {searching && <div className="search-loading" role="status"><span className="spinner" />Searching patient records…</div>}
          {!searching && !query.trim() && <div className="search-guidance"><span className="guidance-mark" aria-hidden="true">⌕</span><strong>Search the patient directory</strong><span>Use an MRN or start typing a patient’s name.</span></div>}
          {!searching && query.trim() && results.length === 0 && <div className="empty search-empty">No patients match “{query.trim()}”.</div>}
          {!searching && results.map(patient => <button type="button" className="patient-result" key={patient.id} onClick={() => { setSelected(patient); setActionError(null); setTransferOpen(false) }}>
            <span className="avatar small-avatar">{initials(patient)}</span><span className="patient-result-copy"><strong>{fullName(patient)}</strong><span><code>{patient.mrn}</code> · DOB {date(patient.dateOfBirth)}</span></span><span className="result-arrow" aria-hidden="true">→</span>
          </button>)}
        </div>
        {searchError ? <ErrorAlert error={searchError} /> : null}
      </section>

      <section className="card patient-panel admit-panel">
        <div className="panel-heading"><div><p className="panel-kicker">NEW EPISODE</p><h3>Admit patient</h3></div><span className="panel-icon panel-icon-teal" aria-hidden="true">＋</span></div>
        <p className="panel-copy">Register a new patient and assign their first destination ward.</p>
        {canWrite ? <form className="admit-form" onSubmit={submitAdmit}>
          <label>MRN<input required maxLength={64} value={admit.mrn} onChange={event => setAdmit({ ...admit, mrn: event.target.value })} /></label>
          <div className="admit-form-row">
            <label>Given name<input required maxLength={100} value={admit.givenName} onChange={event => setAdmit({ ...admit, givenName: event.target.value })} /></label>
            <label>Family name<input required maxLength={100} value={admit.familyName} onChange={event => setAdmit({ ...admit, familyName: event.target.value })} /></label>
          </div>
          <label>Date of birth<input required type="date" value={admit.dateOfBirth} onChange={event => setAdmit({ ...admit, dateOfBirth: event.target.value })} /></label>
          <label>Destination ward<select required value={admit.ward} onChange={event => setAdmit({ ...admit, ward: event.target.value })}><option value="">Select ward…</option>{wards.map(ward => <option key={ward.id} value={ward.code} disabled={ward.freeBeds === 0}>{ward.name} · {ward.freeBeds} beds free</option>)}</select></label>
          <button className="btn admit-button" type="submit" disabled={busy || !admit.mrn.trim() || !admit.givenName.trim() || !admit.familyName.trim() || !admit.dateOfBirth || !admit.ward}><span aria-hidden="true">＋</span>{busy ? ' Registering and admitting…' : ' Register & admit patient'}</button>
        </form> : <p className="read-only-note">You have read-only access. A clinician can admit a patient.</p>}
        <ErrorAlert error={admitError} handled={ADMIT_FIELDS} />
        <div className="admit-note"><span aria-hidden="true">i</span><span>Admissions are assigned to the first available bed in the selected ward.</span></div>
      </section>
    </div> : <Reports admissions={admissions} reports={reports} />}

    {selected && <div className="modal-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) setSelected(null) }}>
      <section className="patient-modal" role="dialog" aria-modal="true" aria-labelledby="patient-modal-title">
        <div className="modal-header"><span className="panel-kicker">PATIENT RECORD</span><button className="modal-close" type="button" aria-label="Close patient details" onClick={() => setSelected(null)}>×</button></div>
        <div className="patient-identity"><span className="avatar large-avatar">{initials(selected)}</span><div><h3 id="patient-modal-title">{fullName(selected)}</h3><p><code>{selected.mrn}</code><span className="identity-separator">·</span>{activeAdmission ? <span className="badge ok">Currently admitted</span> : <span className="badge muted">Not admitted</span>}</p></div></div>
        <div className="patient-details"><div><span>Medical record number</span><strong>{selected.mrn}</strong></div><div><span>Date of birth</span><strong>{date(selected.dateOfBirth)}</strong></div><div><span>Registered</span><strong>{date(selected.registeredAt)}</strong></div>{activeAdmission && <div><span>Current ward</span><strong>{activeAdmission.ward}</strong></div>}</div>
        {activeAdmission && <div className="current-stay"><div><span className="panel-kicker">CURRENT STAY</span><strong>{activeAdmission.ward}</strong><span>Admitted {date(activeAdmission.admittedAt)}</span></div><button className="btn ghost sm" type="button" onClick={() => { onLocate(selected.id); setSelected(null) }}>⌖ Locate in hospital</button></div>}
        {actionError ? <ErrorAlert error={actionError} /> : null}
        {transferOpen && activeAdmission ? <form className="transfer-form" onSubmit={transfer}><label>Transfer to<select required value={destination} onChange={event => setDestination(event.target.value)}><option value="">Select destination ward…</option>{wards.filter(ward => ward.code !== activeAdmission.ward).map(ward => <option key={ward.id} value={ward.code} disabled={ward.freeBeds === 0}>{ward.name} · {ward.freeBeds} beds free</option>)}</select></label><div className="modal-actions"><button className="btn ghost" type="button" disabled={busy} onClick={() => setTransferOpen(false)}>Cancel</button><button className="btn" type="submit" disabled={busy || !destination}>{busy ? 'Transferring…' : 'Confirm transfer'}</button></div></form> : <div className="modal-actions">{!activeAdmission && <button className="btn ghost" type="button" onClick={() => { onLocate(selected.id); setSelected(null) }}>⌖ Locate in hospital</button>}{canWrite && activeAdmission && <><button className="btn ghost" type="button" disabled={busy} onClick={() => { setDestination(''); setTransferOpen(true) }}>⇄ Transfer</button><button className="btn danger-button" type="button" disabled={busy} onClick={discharge}>{busy ? 'Discharging…' : 'Discharge'}</button></>}</div>}
      </section>
    </div>}
  </section>
}

type ReportProps = { admissions: Admission[]; reports: { wardCounts: [string, number][]; completed: Admission[]; averageStay: number | null } }

function Reports({ admissions, reports }: ReportProps) {
  return <section className="reports-view" data-region="reports">
    <div className="reports-intro"><div><p className="panel-kicker">OPERATIONAL SUMMARY</p><h3>Admission reports</h3><p className="hint">A live summary of admission activity from the current hospital record.</p></div><span className="report-date">Updated {new Date().toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}</span></div>
    <div className="report-stat-grid"><div className="report-stat accent"><span>Active admissions</span><strong>{admissions.filter(a => a.status === 'Admitted').length}</strong><small>patients in care now</small></div><div className="report-stat"><span>Total episodes</span><strong>{admissions.length}</strong><small>all recorded admissions</small></div><div className="report-stat"><span>Discharged</span><strong>{reports.completed.length}</strong><small>completed episodes</small></div><div className="report-stat"><span>Average stay</span><strong>{reports.averageStay === null ? '—' : `${reports.averageStay.toFixed(1)}d`}</strong><small>across discharged patients</small></div></div>
    <div className="report-columns"><section className="card report-card"><div className="panel-heading"><div><p className="panel-kicker">CAPACITY VIEW</p><h3>Active by ward</h3></div></div>{reports.wardCounts.length === 0 ? <div className="empty">No active admissions.</div> : <div className="ward-report-list">{reports.wardCounts.map(([ward, count]) => <div className="ward-report-row" key={ward}><span>{ward}</span><span className="ward-bar"><i style={{ width: `${Math.max(8, count / Math.max(...reports.wardCounts.map(([, value]) => value)) * 100)}%` }} /></span><strong>{count}</strong></div>)}</div>}</section><section className="card report-card"><div className="panel-heading"><div><p className="panel-kicker">RECENT ACTIVITY</p><h3>Latest admissions</h3></div></div>{admissions.length === 0 ? <div className="empty">No admission history yet.</div> : <div className="recent-admissions">{admissions.slice(0, 5).map(admission => <div className="recent-row" key={admission.id}><span className={`activity-dot ${admission.status === 'Admitted' ? 'active' : ''}`} /><span><strong>{admission.ward}</strong><small>{date(admission.admittedAt)}</small></span><span className={`badge ${admission.status === 'Admitted' ? 'ok' : 'muted'}`}>{admission.status}</span></div>)}</div>}</section></div>
  </section>
}
