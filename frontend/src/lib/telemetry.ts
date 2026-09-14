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

// The event shape is the backend's JSON Schema, generated into contracts.ts - not restated here.
import type { ClientEvent } from './contracts.ts'

export type { ClientEvent }

/**
 * What this client actually queues. The contract makes the timeline fields optional, because the
 * API accepts a batch from an older tab that has none; `track` always sets them, and the recorder
 * relies on that, so the queue is typed for what it holds rather than for what the wire allows.
 */
export type RecordedEvent = ClientEvent & Required<Pick<ClientEvent, 'seq' | 't'>>

/**
 * What an event may carry. The API bounds `props` to flat scalars and 1024-character strings, and
 * rejects the *whole batch* when one value breaks that - a 400 this client deliberately swallows,
 * so a violation costs a session's telemetry silently. Naming the type here is what turns that into
 * a compile error at the `track` call instead.
 */
export type EventProps = NonNullable<ClientEvent['props']>

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

let queue: RecordedEvent[] = []
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

/** The API's own ceiling on a string value; a longer one fails the batch rather than itself. */
const MAX_PROP_CHARS = 1024

export function track(name: string, props?: EventProps) {
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
    props: bounded(dropped > 0 ? { ...props, droppedSinceLastEvent: takeDropped() } : props),
  })
  if (queue.length >= MAX_BATCH) flush()
}

/**
 * Clips string values to the length the API accepts. Lengths are what types cannot police, and the
 * unbounded ones are the interesting ones - an `app.error` message, a `String(reason)` from a
 * rejected promise. A clipped value still describes the event; an unclipped one takes the batch
 * around it down with it.
 */
function bounded(props: EventProps | undefined): EventProps | undefined {
  if (!props) return props
  let clipped: EventProps | undefined
  for (const [key, value] of Object.entries(props)) {
    if (typeof value !== 'string' || value.length <= MAX_PROP_CHARS) continue
    clipped ??= { ...props }
    clipped[key] = `${value.slice(0, MAX_PROP_CHARS - 1)}…`
  }
  return clipped ?? props
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
export function pendingEvents(): readonly RecordedEvent[] {
  return queue
}
