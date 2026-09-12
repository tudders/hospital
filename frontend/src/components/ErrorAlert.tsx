import { ApiError } from '../lib/api'

export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null
  const problem = error instanceof ApiError ? error.problem : null
  return (
    <div className="alert error" role="alert">
      {problem ? `${problem.title}${problem.detail ? `: ${problem.detail}` : ''}` : String(error)}
      {problem && <span className="corr">correlation {problem.correlationId}</span>}
    </div>
  )
}
