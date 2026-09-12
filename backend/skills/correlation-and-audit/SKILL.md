---
name: correlation-and-audit
description: Use when adding logging, tracing, metrics or audit to an endpoint, when a log line cannot be joined to the request that produced it, or when tracing one user action from the browser through to domain logs. Covers the correlation-id scope, IncludeScopes, [Audited] and OpenTelemetry wiring.
---

# One id joins a click to a log line

`CorrelationIdMiddleware` accepts `X-Correlation-Id` from the client or mints one, echoes it on the
response, tags the trace span with it, and opens a **logging scope** for the rest of the request:

```csharp
using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
```

A scope is ambient: every `ILogger` resolved anywhere inside that request picks it up, so
`PatientService` and `PatientRegisteredHandler` log the correlation id without knowing it exists.
Do not thread a correlation id through method signatures.

Carrying it is half the job. A scope is invisible until a formatter prints it, which is why
`Program.cs` sets `IncludeScopes = true` on the console formatter. The scope is created from a
message template, not a `Dictionary<string, object>`: a dictionary scope is structured but renders as
its own type name. The template is both structured and printable.

`CorrelationTests` asserts both halves - that the scope reaches an ordinary domain log line, and that
the console is configured to render it. Leave both assertions in place.

## Middleware order is load-bearing

Set in `Program.cs`, and this order is the reason failures are still traceable:

1. `UseExceptionHandler` / `UseStatusCodePages` - outermost, so everything below yields problem details, not a bare 500.
2. `CorrelationIdMiddleware` - before routing, so the id exists for every later stage including failures.
3. CORS - before auth, so a rejected pre-flight still answers correctly.
4. Authentication, then Authorization.
5. Routing and model binding.
6. `[Audited]` action filter.
7. Controller action.

Adding middleware means choosing a position and being able to say why it sits there.

## Auditing an action

`[Audited("patient.register")]` decorates the action: it opens its own span, tags user and action,
times the call, and logs user, status and elapsed ms with the correlation id. Put it on any action
that changes clinical state. It is a decorator over the MVC filter chain - it must not change the
action's result.

## The frontend join

`POST /api/telemetry/events` ingests batched browser events. Each carries the session id, a sequence
number and a millisecond offset from session start, plus the correlation id of any API call it made.
So: the alert the user saw prints a correlation id, that id appears in the API logs and in the trace,
and the same id appears in the session's event stream. Keep all three sides of that join intact when
you touch any of them.

## Exporters

Console exporter in Development. Set `Otlp:Endpoint` to ship traces and metrics to a collector. New
instrumentation goes through `Telemetry.ActivitySource` / `Telemetry.MeterName` so it is exported
without extra registration. `/health` is filtered out of traces deliberately.
