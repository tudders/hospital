---
description: Follow one correlation id from the browser event stream through the API logs, trace and audit line
allowed-tools: Bash, Read, Grep, Glob
---

Trace the correlation id in $ARGUMENTS across the system, and report where the chain holds and where
it breaks. With no argument, explain the join and then exercise it live with a fresh request.

The chain, in order:

1. **Browser.** `frontend/src/lib/api.ts` mints a correlation id per request (`newCorrelationId`) and
   sends it as `X-Correlation-Id`, then records an `api.request` event carrying that id, the session
   id, a sequence number and an offset from session start.
2. **Edge.** `CorrelationIdMiddleware` accepts the incoming id (or mints one), echoes it on the
   response, tags the trace span, and opens the logging scope for the request.
3. **Domain logs.** Every `ILogger` inside the request prints the id, because the scope is ambient and
   the console formatter has `IncludeScopes = true`.
4. **Audit.** `[Audited]` logs user, action, status and elapsed ms with the same id, inside its own
   span.
5. **Back to the session.** The browser's batched events reach `POST /api/telemetry/events`, so the
   session timeline and the backend logs share the id.

To exercise it live:

```
dotnet run --project src/Alcidion.Api --launch-profile http
curl -s -D - -H "X-Correlation-Id: trace-demo-1" -H "Content-Type: application/json" \
  -X POST http://localhost:5025/api/auth/login -d '{"username":"nurse","password":"nurse"}'
```

Then repeat a write with the token and the same id, and read the API console output. Confirm all four:
the response header echoes the id, the audit line carries it, an ordinary domain log line carries it,
and the trace span is tagged with it.

Report which links hold, which are missing, and for any break the file and the line that should have
carried the id. If the id is absent from ordinary domain logs, check `IncludeScopes` and that the
scope was created from a message template rather than a dictionary - that is the failure this project
has seen before.
