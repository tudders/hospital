---
name: correlation-ids
description: Use when adding an API call, handling an error, surfacing a message to the user, or changing src/lib/api.ts, telemetry.ts or ErrorAlert. Covers the per-request correlation id, the session id and sequence numbers, and how a UI error is joined to backend logs.
---

# Every request carries an id that the backend echoes

`src/lib/api.ts` mints a fresh correlation id per request (`newCorrelationId()`), sends it as
`X-Correlation-Id`, and records an `api.request` event carrying the id the backend echoed back. The
backend tags its trace span with the same id and stamps every log line in that request through a
logging scope.

That is the whole point of the join: an error the user saw can be found in the API logs, and the
session's event stream can be replayed around it.

## The three ids

| Id | Scope | Stored |
|---|---|---|
| Session id | One browser tab, surviving reloads | `sessionStorage` (`alcidion.sessionId`) |
| Sequence number | Monotonic within the session | `sessionStorage` (`alcidion.seq`) |
| Correlation id | One API request | Not stored; sent, echoed, recorded |

Every recorded event carries the session id, the sequence number and `t`, a millisecond offset from
session start, so a batch that arrives late or out of order still reassembles into the timeline the
user actually experienced. Do not reorder by arrival time, and do not reset the sequence on remount.

## Rules

- **Go through `api()`.** A bare `fetch` has no correlation id, no bearer token, no problem-details
  parsing and no recorded event. If you need a different transport, extend `api()`.
- **Show the id when you show an error.** `ApiError.problem.correlationId` is what makes a user's
  report actionable; `ErrorAlert` prints it. Never swallow it.
- **Errors are recorded too.** A network failure records `api.network_error`; keep that path, since a
  request that never reached the backend leaves no trace on the other side.
- **Read the echoed id, not just the sent one.** The recorded event prefers
  `res.headers.get('X-Correlation-Id')`, so a proxy that replaces the id is visible rather than
  invisible. This requires the backend to expose the header through CORS
  (`WithExposedHeaders`) - if the recorded id always falls back to the sent one, check that first.
- **Never put clinical data in an id, a path or an error message.** See `telemetry-redaction`.

## Tracing one action end to end

1. Reproduce in the browser; copy the correlation id from the error alert, or from the `api.request`
   event in the network payload.
2. Grep the API console output for the id: the audit line, the domain log lines and the span tag all
   carry it.
3. The same id appears in the session's batch posted to `POST /api/telemetry/events`, which is how the
   backend can place the request inside the user's timeline.

If the id is missing from ordinary backend log lines, the failure is on the backend side -
`IncludeScopes` on the console formatter, or a scope created from a dictionary instead of a message
template.
