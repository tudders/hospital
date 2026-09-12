/**
 * Frontend observability transport.
 *
 * - One session id per browser tab (sessionStorage).
 * - One correlation id per API request; it is sent as X-Correlation-Id and the backend echoes it,
 *   tags its trace span with it, and stamps every log line in that request.
 * - Every recorded event carries the session id, a monotonic sequence number and an offset from
 *   session start, so a batch that arrives late or out of order still reassembles into the
 *   timeline the user actually experienced.
 * - Events are batched and posted to /api/telemetry/events; on page hide we flush via sendBeacon.
 *
 * Capture lives in session-recorder.ts. This module only buffers and ships.
 */

export type ClientEvent = {
  name: string
  sessionId: string
  seq: number
  at: string
  /** Milliseconds since the session started, for replaying the timeline at its original pace. */
  t: number
  props?: Record<string, unknown>
}

const SESSION_KEY = 'alcidion.sessionId'
const SEQ_KEY = 'alcidion.seq'
const FLUSH_INTERVAL_MS = 5000
const MAX_BATCH = 50
/** Ceiling on the buffer, so a backend outage cannot grow it without bound. */
const MAX_QUEUE = 500

function readSessionId(): string {
  try {
    const existing = sessionStorage.getItem(SESSION_KEY)
    if (existing) return existing
    const fresh = crypto.randomUUID()
    sessionStorage.setItem(SESSION_KEY, fresh)
    return fresh
  } catch {
    return crypto.randomUUID()
  }
}

export const sessionId = readSessionId()

// Survives a reload within the tab, so sequence numbers stay unique for the session id.
let seq = (() => {
  try {
    return Number(sessionStorage.getItem(SEQ_KEY) ?? 0) || 0
  } catch {
    return 0
  }
})()

const startedAt = performance.now()

export function newCorrelationId(): string {
  return crypto.randomUUID().replace(/-/g, '')
}

let queue: ClientEvent[] = []
let endpoint = ''
let timer: number | undefined
let dropped = 0

export function configureTelemetry(apiBaseUrl: string) {
  endpoint = `${apiBaseUrl}/api/telemetry/events`
  if (timer === undefined) {
    timer = window.setInterval(flush, FLUSH_INTERVAL_MS)
    window.addEventListener('pagehide', () => flush(true))
  }
}

export function track(name: string, props?: Record<string, unknown>) {
  if (queue.length >= MAX_QUEUE) {
    dropped++
    return
  }
  seq++
  try {
    sessionStorage.setItem(SEQ_KEY, String(seq))
  } catch {
    // Private mode: the in-memory counter still orders this session's events.
  }
  queue.push({
    name,
    sessionId,
    seq,
    at: new Date().toISOString(),
    t: Math.round(performance.now() - startedAt),
    props: dropped > 0 ? { ...props, droppedSinceLastEvent: takeDropped() } : props,
  })
  if (queue.length >= MAX_BATCH) flush()
}

function takeDropped() {
  const n = dropped
  dropped = 0
  return n
}

export function flush(useBeacon = false) {
  if (!endpoint || queue.length === 0) return
  const batch = queue
  queue = []
  const body = JSON.stringify(batch)

  if (useBeacon && navigator.sendBeacon) {
    navigator.sendBeacon(endpoint, new Blob([body], { type: 'application/json' }))
    return
  }

  fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Correlation-Id': newCorrelationId() },
    body,
    keepalive: true,
  }).catch(() => {
    // Telemetry must never break the app. Drop on failure.
  })
}

/** Test seam: the queued events, without shipping them. */
export function pendingEvents(): readonly ClientEvent[] {
  return queue
}
