# Lifecycles: a request through the API, a session through the UI

Two lifecycles decide where behaviour belongs in this codebase. Getting them wrong is what produced
most of the defects found in review, so this walks both, in order, and names the place each concern
is handled.

## 1. An HTTP request through the API

Order is set in `Program.cs` and it is load-bearing.

| Stage | Component | Why it sits here |
|---|---|---|
| 1 | `UseExceptionHandler` / `UseStatusCodePages` | Outermost, so everything below produces problem-details instead of a bare 500. |
| 2 | `CorrelationIdMiddleware` | Before routing, so the id exists for every later stage, including failures. |
| 3 | CORS | Before auth, so a rejected pre-flight still answers correctly. |
| 4 | Authentication then Authorization | Identity must exist before a policy can be evaluated. |
| 5 | Routing and model binding | Binds the command record; malformed JSON is a 400 before any domain code runs. |
| 6 | `[Audited]` action filter | Wraps the action, so it can time it and see its status code. |
| 7 | Controller action | Translates HTTP to a command, and a `Result<T>` back to a status code. Nothing else. |
| 8 | Application service | The use case: validate, enforce invariants, persist, publish. |
| 9 | Aggregate | The rules that are true regardless of who calls: an MRN is normalized, a discharge happens once. |
| 10 | Event bus and handlers | After the write, in-process today, over a broker later. |

### Where the correlation id lives

`CorrelationIdMiddleware` opens a **logging scope** for the duration of the request. A scope is
ambient: every `ILogger` resolved anywhere inside that request picks it up, so `PatientService` and
`PatientRegisteredHandler` log with the correlation id without knowing it exists.

Carrying it is only half the job. A scope is invisible until a formatter is told to print it, which
is why `Program.cs` configures the console with `IncludeScopes = true`, and why the scope is created
from a message template rather than a dictionary:

```csharp
using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
```

A `Dictionary<string, object>` scope is structured but renders as its own type name. The template is
both structured and printable. `CorrelationTests` asserts both halves: that the scope reaches an
ordinary domain log line, and that the console is configured to render it.

### Where invariants live

An invariant belongs at the point of the write, never in a check that precedes it.

```csharp
// Wrong: two concurrent requests both pass the check, both insert.
if (await repo.GetByMrnAsync(cmd.Mrn) is not null) return Conflict();
await repo.AddAsync(patient);

// Right: the insert itself decides.
if (!await repo.TryAddAsync(patient)) return Conflict();
```

`TryAddAsync` and `TryAddActiveAsync` are the only enforcement points for "one patient per MRN" and
"one active admission per patient". In memory they claim a key with `ConcurrentDictionary.TryAdd`;
against a database they map a unique-index violation to the same `false`. `ConcurrentDictionary`
makes each operation atomic, which is not the same as making a check-then-insert pair atomic, and
that gap is exactly what the concurrency tests exercise with sixteen real threads.

The same reasoning applies inside the aggregate: `Admission.Discharge` takes its check and its write
under one lock, so two concurrent discharges cannot both publish a `PatientDischarged` event.

### Where normalization lives

`Patient.NormalizeMrn` is on the aggregate, and both the stored value and the uniqueness key go
through it. When normalization lived only in the constructor, `"REVIEW-1"` and `" review-1 "`
compared as different MRNs and stored as the same one.

### DI lifetimes

Registered in each domain's `ServiceCollectionExtensions`. Repositories are singletons only because
they are in-memory stand-ins for a database; application services are scoped, so they live exactly as
long as the request. `TestServices.BuildRealWiring` builds the container with `ValidateOnBuild` and
`ValidateScopes`, which turns a captive-dependency mistake into a failing test rather than a
production surprise.

## 2. A session through the UI

React's lifecycle is: render (pure, may run more than once), commit, effects, cleanup. Anything that
touches the outside world belongs in an effect with a cleanup, and must tolerate running twice.

### Hooks in use

- `useState` for local form and page state.
- `useCallback` for `refresh`, because it is a dependency of an effect; without it the effect would
  re-run on every render.
- `useEffect` with `[]` to start session recording, returning the recorder's teardown as cleanup.
- `useEffect` with `[loggedIn]` to restore a session from a stored token.

### What StrictMode caught

In development, StrictMode deliberately mounts, unmounts and remounts every component to expose
effects that are not idempotent. It found two real bugs here, both visible in the recorded timeline
as duplicate events:

1. `session.start` was emitted per effect mount. A session starts once per tab, so the marker now
   lives in `sessionStorage` beside the session id, and survives both a remount and a reload.
2. `page.view` was emitted per effect run, duplicating what `session.start` already recorded. It was
   removed; real navigations are recorded as `ui.navigate`.

The general rule: an effect that emits an event must key that event to the thing it actually
describes, not to the component's mounting.

### The recording lifecycle

`startSessionRecording` attaches passive capture listeners and returns a stop function, so the
recorder's lifetime is tied to the component that owns it. Each event gets a sequence number and a
millisecond offset from session start, which is what makes the timeline replayable even when batches
arrive late or out of order. `session-recorder.ts` decides *when* to record; `redaction.ts` decides
*what may be said*; `telemetry.ts` only buffers and ships. The redaction rules are unit-tested
without a DOM because they are the part that must never regress: element text and field values are
patient data, so an element is described by role, name and region, and a typed value is reduced to
its length.

## 3. Where the referenced skills landed

| Reference in `notes.md` | What it shaped |
|---|---|
| .NET minimal hosting and OpenAPI | `Program.cs`: `AddOpenApi`, `MapOpenApi` in Development only, problem-details everywhere. |
| React best practices | Effects with cleanup, `useCallback` for effect dependencies, no state derived in render, StrictMode left on. |
| Lifecycle and hooks | This document, and the two StrictMode bugs above. |
| SOLID | Services depend on abstractions; each bounded context owns its repository interface; `Result<T>` keeps expected failures out of exceptions. |
| Gang of Four | Observer for the event bus and handlers, Repository for persistence, Decorator for `[Audited]` over the action pipeline. The static `Register`/`Admit` methods are static factory methods, not the GoF Factory Method pattern, and are labelled as such. |
| TDD | Each fix in this round was written as a failing test first: `Register_with_whitespace_padded_duplicate_mrn_is_a_conflict`, the two race tests, and `Ordinary_domain_logs_carry_the_correlation_id`. |
| Performance and bundle size | `npm run size` enforces a gzip budget on every build. |
