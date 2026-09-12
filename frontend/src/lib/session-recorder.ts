/**
 * Session recording.
 *
 * What the backend gets is an ordered timeline of what the user did: pointer, keyboard, form,
 * navigation, visibility and viewport events, each stamped with the session id, a sequence number
 * and a millisecond offset from session start. Replaying a session means walking that timeline.
 *
 * What it deliberately never gets is content. This is a clinical app, so field values, option text
 * and element text are treated as patient data: an element is described by its role, name and
 * position, and a typed value is reduced to its length. Nothing recorded here can reconstruct an
 * MRN, a name or a date of birth.
 *
 * DOM mutation capture is out of scope; the timeline plus the rendered app is enough to retrace a
 * session without shipping a full DOM-diff recorder.
 */

import { describeField, describeTarget, type ElementLike } from './redaction'
import { track } from './telemetry'

const KEYS_WORTH_RECORDING = new Set(['Enter', 'Escape', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'])
const SCROLL_THROTTLE_MS = 250
const RESIZE_THROTTLE_MS = 250

function throttle<T extends unknown[]>(ms: number, fn: (...args: T) => void): (...args: T) => void {
  let last = 0
  return (...args: T) => {
    const now = Date.now()
    if (now - last < ms) return
    last = now
    fn(...args)
  }
}

const STARTED_KEY = 'alcidion.recording.started'

let started = false

/**
 * session.start marks the beginning of the session, not of a React effect. StrictMode remounts and
 * in-tab reloads both re-run this module, and the session id outlives them, so the marker is kept
 * beside it rather than in a module variable.
 */
function markSessionStartOnce(props: Record<string, unknown>) {
  try {
    if (sessionStorage.getItem(STARTED_KEY)) return
    sessionStorage.setItem(STARTED_KEY, '1')
  } catch {
    // Private mode: a duplicated marker is better than losing the start of the timeline.
  }
  track('session.start', props)
}

/** Attaches the recorder. Idempotent, and returns a stop function for tests and teardown. */
export function startSessionRecording(): () => void {
  if (started || typeof document === 'undefined') return () => {}
  started = true

  const offs: Array<() => void> = []
  const on = <K extends keyof DocumentEventMap>(
    target: Document | Window,
    type: K | string,
    handler: (e: never) => void,
    opts?: AddEventListenerOptions,
  ) => {
    target.addEventListener(type, handler as EventListener, { passive: true, capture: true, ...opts })
    offs.push(() => target.removeEventListener(type, handler as EventListener, { capture: true }))
  }

  markSessionStartOnce({
    url: location.pathname + location.search,
    viewport: `${window.innerWidth}x${window.innerHeight}`,
    devicePixelRatio: window.devicePixelRatio,
    language: navigator.language,
  })

  on(document, 'click', (e: MouseEvent) => {
    const el = e.target as unknown as ElementLike | null
    track('ui.click', { target: describeTarget(el), x: Math.round(e.clientX), y: Math.round(e.clientY) })
  })

  on(document, 'change', (e: Event) => {
    const el = e.target as unknown as (ElementLike & { value?: unknown }) | null
    if (!el) return
    track('ui.change', describeField(el, el.value))
  })

  on(document, 'submit', (e: Event) => {
    track('ui.submit', { target: describeTarget(e.target as unknown as ElementLike) })
  })

  // Character keys are never recorded: a keystroke stream would reconstruct the field contents.
  on(document, 'keydown', (e: KeyboardEvent) => {
    if (!KEYS_WORTH_RECORDING.has(e.key)) return
    track('ui.key', { key: e.key, target: describeTarget(e.target as unknown as ElementLike) })
  })

  on(
    document,
    'scroll',
    throttle(SCROLL_THROTTLE_MS, () => track('ui.scroll', { y: Math.round(window.scrollY) })),
  )

  on(
    window,
    'resize',
    throttle(RESIZE_THROTTLE_MS, () => track('ui.resize', { viewport: `${window.innerWidth}x${window.innerHeight}` })),
  )

  on(document, 'visibilitychange', () => track('ui.visibility', { state: document.visibilityState }))

  on(window, 'popstate', () => track('ui.navigate', { url: location.pathname + location.search }))

  on(window, 'error', (e: ErrorEvent) => {
    track('app.error', { message: e.message, source: e.filename, line: e.lineno })
  })

  on(window, 'unhandledrejection', (e: PromiseRejectionEvent) => {
    track('app.unhandled_rejection', { message: String(e.reason) })
  })

  return () => {
    for (const off of offs) off()
    offs.length = 0
    started = false
  }
}
