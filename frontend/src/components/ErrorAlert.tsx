import { ApiError, fieldErrors } from '../lib/api'

type Props = {
  error: unknown
  /**
   * Fields the caller already marks on its own inputs. Their messages are left out here rather
   * than shown twice - the input is the better place to read "the mrn field is required".
   */
  handled?: readonly string[]
}

export function ErrorAlert({ error, handled = [] }: Props) {
  if (!error) return null
  const problem = error instanceof ApiError ? error.problem : null
  const unhandled = Object.entries(fieldErrors(error)).filter(([field]) => !handled.includes(field))

  return (
    <div className="alert error" role="alert">
      {problem ? `${problem.title}${problem.detail ? `: ${problem.detail}` : ''}` : String(error)}
      {unhandled.length > 0 && (
        <ul className="field-errors">
          {unhandled.flatMap(([field, messages]) =>
            messages.map((message, i) => <li key={`${field}:${i}`}>{message}</li>),
          )}
        </ul>
      )}
      {problem && <span className="corr">correlation {problem.correlationId}</span>}
    </div>
  )
}
