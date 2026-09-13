import { useEffect, useMemo, useState } from 'react'
import { api } from '../lib/api'
import { hospitalHierarchy, localDateTime, occupancy, sampleHospital, SAMPLE_TIME, type HospitalBed, type HospitalSnapshot } from '../lib/hospital'
import { ErrorAlert } from './ErrorAlert'
import { HospitalModel, type Inspection } from './HospitalModel'

const formatTime = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })

export function Hospital() {
  const [source, setSource] = useState<'sql' | 'sample'>('sql')
  const [snapshot, setSnapshot] = useState<HospitalSnapshot | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(true)
  const [at, setAt] = useState<string | null>(null)
  const [draftAt, setDraftAt] = useState(localDateTime(new Date()))
  const [refresh, setRefresh] = useState(0)
  const [autoRefresh, setAutoRefresh] = useState(true)
  const [hospitalId, setHospitalId] = useState('')
  const [floorId, setFloorId] = useState('all')
  const [wardId, setWardId] = useState('')
  const [roomId, setRoomId] = useState('')
  const [bedId, setBedId] = useState('')
  const [hovered, setHovered] = useState<Inspection | null>(null)
  const [angle, setAngle] = useState(30)
  const [exploded, setExploded] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    async function load() {
      setBusy(true)
      try {
        const next = source === 'sample' ? sampleHospital(at ?? SAMPLE_TIME) :
          await api<HospitalSnapshot>(`/api/hospital-occupancy${at ? `?at=${encodeURIComponent(at)}` : ''}`, { signal: controller.signal })
        if (!controller.signal.aborted) { setSnapshot(next); setHovered(null); setError(null) }
      } catch (err) {
        if (!controller.signal.aborted) setError(err)
      } finally {
        if (!controller.signal.aborted) {
          setBusy(false)
          if (source === 'sql' && !at && autoRefresh) timer = setTimeout(() => { void load() }, 15_000)
        }
      }
    }
    void load()
    return () => { controller.abort(); clearTimeout(timer) }
  }, [source, at, refresh, autoRefresh])

  const hospitals = useMemo(() => hospitalHierarchy(snapshot?.beds ?? []), [snapshot])
  const hospital = hospitals.find(h => h.id === hospitalId) ?? hospitals[0]
  const floor = hospital?.floors.find(f => f.id === floorId)
  const detailFloor = floor ?? hospital?.floors[0]
  const ward = detailFloor?.wards.find(w => w.id === wardId) ?? detailFloor?.wards[0]
  const room = ward?.rooms.find(r => r.id === roomId) ?? ward?.rooms[0]
  const bed = room?.beds.find(b => b.id === bedId)
  const counts = occupancy(hospital?.beds ?? [])
  const selected: Inspection | null = bed ? { label: `Bed ${bed.number} · ${bed.status}`, path: `${detailFloor?.name} / ${ward?.name} / ${room?.name}`, beds: [bed] } :
    room && roomId ? { label: room.name, path: `${detailFloor?.name} / ${ward?.name}`, beds: room.beds } :
    ward && wardId ? { label: ward.name, path: detailFloor?.name ?? '', beds: ward.beds } :
    floor ? { label: floor.name, path: hospital.name, beds: floor.beds } :
    hospital ? { label: hospital.name, path: 'Whole hospital', beds: hospital.beds } : null
  const inspection = hovered ?? selected
  const inspected = occupancy(inspection?.beds ?? [])

  function chooseFloor(id: string) { setFloorId(id); setWardId(''); setRoomId(''); setBedId(''); setHovered(null) }
  function selectBed(next: HospitalBed) {
    setFloorId(next.floorId); setWardId(next.wardId); setRoomId(next.roomId); setBedId(next.id); setHovered(null)
  }
  function changeSource(next: 'sql' | 'sample') {
    setSource(next); setSnapshot(null); setError(null); setBusy(true); setAt(null); chooseFloor('all')
    setDraftAt(localDateTime(new Date(next === 'sample' ? SAMPLE_TIME : Date.now())))
  }

  return <section className="hospital-page" data-region="hospital">
    <div className="hospital-heading">
      <div><p className="hospital-eyebrow">OPERATIONS / SPATIAL OVERVIEW</p><h2>Hospital at a glance<span className="hospital-title-dot">.</span></h2>
        <p className="hint">Every floor. Every room. A clearer picture of capacity.</p></div>
      <div className={`hospital-freshness ${error ? 'is-stale' : ''}`} role="status"><span className="freshness-dot" />
        {source === 'sample' ? 'Sample data · not live' : error ? 'Unavailable · snapshot may be stale' : busy ? 'Reading snapshot…' : at ? 'Historical snapshot' : autoRefresh ? 'Auto-refresh · every 15s' : 'Snapshot · refresh paused'}
      </div>
    </div>

    <div className="hospital-toolbar card">
      <div className="hospital-source"><span className="hospital-eyebrow">DATA SOURCE</span>
        <select aria-label="Occupancy data source" data-track="hospital-source" value={source} onChange={e => changeSource(e.target.value as 'sql' | 'sample')}>
          <option value="sql">Hospital database</option><option value="sample">Sample hospital</option>
        </select>
      </div>
      <form className="hospital-time-form" data-track="hospital-snapshot-time" onSubmit={e => {
        e.preventDefault(); setAt(new Date(draftAt).toISOString()); setHovered(null); setRefresh(n => n + 1)
      }}>
        <label>Occupancy at local time<input type="datetime-local" required aria-label="Snapshot local date and time" data-track="hospital-time" value={draftAt} max={localDateTime(new Date())} onChange={e => setDraftAt(e.target.value)} /></label>
        <button className="btn ghost sm" data-track="load-historical-snapshot" disabled={busy}>View time</button>
      </form>
      <button className="btn ghost sm" data-track="hospital-current-snapshot" onClick={() => {
        setDraftAt(localDateTime(new Date(source === 'sample' ? SAMPLE_TIME : Date.now())))
        setAt(null); setRefresh(n => n + 1)
      }} disabled={busy}>{source === 'sample' ? 'Reset sample' : 'Now'}</button>
      {source === 'sql' && <label className="hospital-auto"><input type="checkbox" data-track="hospital-auto-refresh" checked={autoRefresh} disabled={!!at} onChange={e => setAutoRefresh(e.target.checked)} /> Auto-refresh</label>}
      <button className="btn sm" data-track="refresh-hospital" onClick={() => setRefresh(n => n + 1)} disabled={busy}>{busy ? 'Loading…' : '↻ Refresh'}</button>
    </div>
    <ErrorAlert error={error} />
    {source === 'sample' && <p className="hospital-sample-notice">Sample preview — synthetic occupancy for exploring the 720-bed model. These counts are not database results.</p>}

    {!snapshot ? <div className="card hospital-load" role="status"><div className="hospital-load-icon">▥</div><h3>{busy ? 'Building your hospital view…' : 'Hospital data is unavailable'}</h3>
      <p className="hint">{busy ? 'Reading the hierarchy and occupancy snapshot.' : 'Retry the database connection or explore the model with clearly labelled sample data.'}</p>
      {!busy && <button className="btn" data-track="explore-sample-hospital" onClick={() => changeSource('sample')}>Explore sample hospital</button>}
    </div> : !hospital ? <div className="card empty">No hospital beds were returned for this snapshot.</div> : <>
      {hospitals.length > 1 && <label>Hospital<select aria-label="Hospital" data-track="select-hospital" value={hospital.id} onChange={e => { setHospitalId(e.target.value); chooseFloor('all') }}>{hospitals.map(h => <option key={h.id} value={h.id}>{h.name}</option>)}</select></label>}
      <div className="hospital-stats">
        <div className="hospital-stat"><span>Total beds</span><strong>{counts.total}<small>across {hospital.floors.length} floors</small></strong></div>
        <div className="hospital-stat occupied"><span><i />Occupied</span><strong>{counts.occupied}<small>{counts.percent}% of active inventory</small></strong></div>
        <div className="hospital-stat available"><span><i />Available</span><strong>{counts.available}<small>unoccupied & unblocked</small></strong></div>
        <div className="hospital-stat blocked"><span><i />Blocked / inactive</span><strong>{counts.blocked + counts.inactive}<small>{counts.blocked} blocked · {counts.inactive} inactive</small></strong></div>
      </div>

      <div className="hospital-workspace">
        <div className="hospital-scene card" data-region="hospital-model">
          <div className="hospital-scene-header"><div><h3>{hospital.name}</h3><p className="hint">{floor ? floor.name : 'All floors'} <span aria-hidden="true">/</span> {floor ? floor.wards.length : hospital.floors.reduce((n, f) => n + f.wards.length, 0)} wards <span aria-hidden="true">/</span> {floor ? floor.beds.length : counts.total} beds</p></div>
            <span className="hospital-view-tag">3D OCCUPANCY</span></div>
          <div className="hospital-floor-tabs" aria-label="Floor selection">
            <button className={floorId === 'all' ? 'active' : ''} data-track="view-all-floors" aria-pressed={floorId === 'all'} onClick={() => chooseFloor('all')}>All floors</button>
            {hospital.floors.map(f => <button key={f.id} className={floorId === f.id ? 'active' : ''} data-track="view-floor" aria-pressed={floorId === f.id} onClick={() => chooseFloor(f.id)}>Level {f.number}</button>)}
          </div>
          <HospitalModel hospital={hospital} floorId={floorId} angle={angle} exploded={exploded} onInspect={setHovered} onSelect={selectBed} />
          <div className="hospital-scene-controls">
            <div><button className="btn ghost sm" data-track="rotate-hospital-left" aria-label="Rotate hospital left" onClick={() => setAngle(a => Math.max(-35, a - 15))}>↶</button>
              <button className="btn ghost sm" data-track="rotate-hospital-right" aria-label="Rotate hospital right" onClick={() => setAngle(a => Math.min(80, a + 15))}>↷</button>
              <button className="btn ghost sm" data-track="reset-hospital-camera" onClick={() => { setAngle(30); setExploded(true); chooseFloor('all') }}>Reset view</button></div>
            <button className="btn ghost sm" data-track="toggle-exploded-view" aria-pressed={exploded} disabled={floorId !== 'all'} onClick={() => setExploded(v => !v)}>{exploded ? '↕ Exploded' : '▤ Compact'}</button>
          </div>
          <div className="hospital-scene-footer"><span>Hover to inspect · click a bed to explore</span><span className="hospital-legend"><span className="occupied"><i />Occupied</span><span className="available"><i />Available</span><span className="blocked"><i />Blocked</span><span className="inactive"><i />Inactive</span></span></div>
        </div>

        <aside className="hospital-sidebar" data-region="hospital-inspector">
          <section className="card hospital-inspection" aria-live="polite" aria-atomic="true">
            <p className="hospital-eyebrow">{hovered ? 'HOVER INSPECTION' : 'OCCUPANCY SNAPSHOT'}</p>
            <p className="hint hospital-path">{inspection?.path}</p><h3>{inspection?.label}</h3>
            <div className="hospital-inspection-number">{inspected.occupied}<span>/ {inspected.total} beds occupied</span></div>
            <div className="hospital-capacity-bar" aria-hidden="true">{(['occupied', 'available', 'blocked', 'inactive'] as const).map(s => <span key={s} className={s} style={{ width: `${inspected.total ? inspected[s] / inspected.total * 100 : 0}%` }} />)}</div>
            <div className="hospital-inspection-counts"><span>{inspected.available} available</span><span>{inspected.blocked} blocked</span><span>{inspected.inactive} inactive</span></div>
            <p className="hint hospital-snapshot-stamp">As of {formatTime(snapshot.asOf)}</p>
          </section>
          <section className="card hospital-explorer">
            <p className="hospital-eyebrow">EXPLORE THE HOSPITAL</p>
            <label>Floor<select data-track="explorer-floor" aria-label="Explore floor" value={detailFloor?.id ?? ''} onChange={e => chooseFloor(e.target.value)}>{hospital.floors.map(f => <option key={f.id} value={f.id}>{f.name} · {occupancy(f.beds).occupied}/{f.beds.length} occupied</option>)}</select></label>
            <label>Ward<select data-track="explorer-ward" aria-label="Explore ward" value={ward?.id ?? ''} onChange={e => { setFloorId(detailFloor!.id); setWardId(e.target.value); setRoomId(''); setBedId(''); setHovered(null) }}>{detailFloor?.wards.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}</select></label>
            <p className="hospital-field-label">Rooms <span>{ward?.rooms.length} rooms · {ward?.beds.length} beds</span></p>
            <div className="hospital-rooms">{ward?.rooms.map(r => <button key={r.id} className={room?.id === r.id ? 'selected' : ''} data-track="explore-room" aria-pressed={room?.id === r.id}
              onFocus={() => setHovered(null)} onClick={() => { setFloorId(detailFloor!.id); setWardId(ward.id); setRoomId(r.id); setBedId('') }}><span>{r.name}</span><strong>{occupancy(r.beds).occupied}<small>/{r.beds.length}</small></strong></button>)}</div>
            <p className="hospital-field-label">{room?.name} <span>Select a bed</span></p>
            <div className="hospital-beds">{room?.beds.map(b => <button key={b.id} className={`hospital-bed ${b.status} ${b.id === bedId ? 'selected' : ''}`} data-track="explore-bed" aria-pressed={b.id === bedId} onClick={() => selectBed(b)}><span className="hospital-bed-symbol" aria-hidden="true">▰</span><strong>Bed {b.number}</strong><small>{b.status}</small></button>)}</div>
            <p className="hint">Occupancy only. Patient identities are not included.</p>
          </section>
        </aside>
      </div>
      <footer className="hospital-footnote"><span>Snapshot captured {formatTime(snapshot.capturedAt)} · Times shown in {Intl.DateTimeFormat().resolvedOptions().timeZone}</span><span>Physical inventory; availability does not include staffing or clinical suitability.</span></footer>
    </>}
  </section>
}
