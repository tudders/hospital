import { getToken } from './auth'
import { newCorrelationId, track } from './telemetry'

export const API_BASE = (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5025'

/** RFC 9457 problem details, as returned by the backend for expected failures. */
export type ProblemDetails = {
  status: number
  title: string
  detail?: string
  correlationId: string
  /**
   * Per-field validation messages, keyed by the field name as it was sent - `mrn`, `props.viewport`,
   * `[1].name`. Present on a 400 the API validated at the edge; absent on every other failure, and
   * on a 400 that came from a domain rule with no field to blame.
   */
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  problem: ProblemDetails
  constructor(problem: ProblemDetails) {
    super(problem.detail ?? problem.title)
    this.problem = problem
  }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const correlationId = newCorrelationId()
  const started = performance.now()
  const headers = new Headers(init.headers)
  // Keep snapshot times and future clinical query values out of session telemetry.
  const telemetryPath = path.split('?')[0]
  headers.set('X-Correlation-Id', correlationId)
  headers.set('Accept', 'application/json')
  if (init.body) headers.set('Content-Type', 'application/json')
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  let res: Response
  try {
    res = await fetch(`${API_BASE}${path}`, { ...init, headers })
  } catch (err) {
    if (init.signal?.aborted) throw err
    track('api.network_error', { path: telemetryPath, correlationId })
    throw new ApiError({ status: 0, title: 'Network error', detail: 'Could not reach the API.', correlationId })
  }

  track('api.request', {
    path: telemetryPath,
    method: init.method ?? 'GET',
    status: res.status,
    durationMs: Math.round(performance.now() - started),
    correlationId: res.headers.get('X-Correlation-Id') ?? correlationId,
  })

  if (res.ok) {
    return res.status === 204 ? (undefined as T) : ((await res.json()) as T)
  }

  let problem: Partial<ProblemDetails> = {}
  try {
    problem = await res.json()
  } catch {
    // non-JSON error body
  }
  throw new ApiError({
    status: res.status,
    title: problem.title ?? res.statusText,
    detail: problem.detail,
    correlationId,
    errors: problem.errors,
  })
}

/**
 * The per-field messages on a failure, or an empty object if it carried none. Lets a form ask
 * `fieldErrors(err).mrn` without first proving the failure was a validation one.
 */
export function fieldErrors(error: unknown): Record<string, string[]> {
  return error instanceof ApiError ? (error.problem.errors ?? {}) : {}
}
