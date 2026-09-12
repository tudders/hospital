import { getToken } from './auth'
import { newCorrelationId, track } from './telemetry'

export const API_BASE = (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5025'

/** RFC 9457 problem details, as returned by the backend for expected failures. */
export type ProblemDetails = {
  status: number
  title: string
  detail?: string
  correlationId: string
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
  headers.set('X-Correlation-Id', correlationId)
  headers.set('Accept', 'application/json')
  if (init.body) headers.set('Content-Type', 'application/json')
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  let res: Response
  try {
    res = await fetch(`${API_BASE}${path}`, { ...init, headers })
  } catch (err) {
    track('api.network_error', { path, correlationId, message: String(err) })
    throw new ApiError({ status: 0, title: 'Network error', detail: 'Could not reach the API.', correlationId })
  }

  track('api.request', {
    path,
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
  })
}
