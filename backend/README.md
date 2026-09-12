# Alcidion API (backend)

ASP.NET Core 9 Web API. Deployed to SmarterASP.NET (Windows Server 2022 / IIS 10, Web Deploy or GitHub workflow deploy; .NET 9 supported).

## Layout

```
src/
  Alcidion.Shared              shared kernel: IDomainEvent, IEventBus (Mediator), IEventHandler<T> (Observer), Result<T>, IClock
  Alcidion.Patients.Contracts  events Patients publishes (the only thing other domains may reference)
  Alcidion.Patients            Patients bounded context: Patient aggregate, repository, PatientService
  Alcidion.Admissions          Admissions bounded context: Admission aggregate, KnownPatients read model, AdmissionService
  Alcidion.Api                 host: controllers, JWT auth, correlation-id middleware, OpenTelemetry, [Audited] attribute
tests/
  Alcidion.Domain.Tests        unit tests per domain + cross-domain event flow with real DI wiring
  Alcidion.Api.Tests           integration tests via WebApplicationFactory (auth, correlation, end-to-end flow)
```

Admissions never calls Patients. It learns about patients by handling `PatientRegistered` and keeping its own read model, so it can be split into a separate service by swapping `InMemoryEventBus` for a broker.

## Run

```
dotnet test
dotnet run --project src/Alcidion.Api --launch-profile http    # http://localhost:5025
```

OpenAPI: `GET /openapi/v1.json` (Development only). Health: `GET /health`.

## Auth

JWT bearer, symmetric dev key in `appsettings.json` (`Jwt:Secret`; override in production). `POST /api/auth/login` issues tokens for demo users:

| user   | password | roles     |
|--------|----------|-----------|
| nurse  | nurse    | clinician |
| doctor | doctor   | clinician |
| admin  | admin    | admin     |
| viewer | viewer   | none      |

Reads need any authenticated user. Writes need the `Clinician` policy (clinician or admin role).

## Invariants

Uniqueness is enforced by the insert, not by a check that precedes it. `IPatientRepository.TryAddAsync`
(one patient per MRN) and `IAdmissionRepository.TryAddActiveAsync` (one active admission per patient)
are the single enforcement points; in memory they claim a key atomically, and against a database they
map a unique-index violation to the same result. MRNs are normalized by `Patient.NormalizeMrn` before
both storage and comparison, so `" review-1 "` and `REVIEW-1` are the same patient. `Admission.Discharge`
takes its check and write under one lock, so a double discharge cannot publish two events.

The concurrency tests run sixteen real threads released by a barrier; `Task.Run` is unsuitable because
blocking on a barrier starves the thread pool and the release is drip-fed.

## Observability

- `X-Correlation-Id` is accepted from the client (or minted), echoed on the response, tagged on the trace span and added to every log line in the request via a logging scope. The console formatter is configured with `IncludeScopes`, so ordinary domain logs print it and not just the audit line.
- OpenTelemetry traces and metrics. Console exporter in Development; set `Otlp:Endpoint` to ship to a collector.
- `[Audited("action")]` on a controller action logs user, status and elapsed time with the correlation id, inside its own span.
- `POST /api/telemetry/events` ingests batched frontend events. Each carries the session id, a sequence number and a millisecond offset from session start, so a browser session can be replayed in order and joined to backend logs by correlation id.

## Publish (IIS / SmarterASP.NET)

```
dotnet publish src/Alcidion.Api -c Release -o publish
```

`dotnet publish` emits `web.config` for the ASP.NET Core IIS module. Upload `publish/` via Web Deploy or FTP. Set `Jwt__Secret` and `Cors__Origins__0` as environment variables or in `appsettings.Production.json`.

## Agent tooling

```
skills/        five playbooks, tool-agnostic: invariants at the write, contexts and events,
               correlation and audit, TDD and verification, Result<T> and problem details
.agents/       AGENTS.md - the rules, commands and layout any coding agent should read first
.claude/       Claude Code config: settings, hooks, two review subagents, /verify /slice /trace
```

`skills/` is the single tracked copy; `.claude/hooks/sync-skills.mjs` mirrors it into
`.claude/skills/` and `.agents/skills/` on session start, and both mirrors are gitignored. Hooks are
Node scripts: they guard credential files and history-rewriting git commands, flag this repo's known
defect classes on every `.cs` write, and run `dotnet test` before a session turn can end when C#
changed. See `.claude/README.md`.
