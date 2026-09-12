# AGENTS.md - Alcidion API

ASP.NET Core 9 Web API, two bounded contexts talking through events. Deploy target is
SmarterASP.NET (Windows Server 2022 / IIS 10).

Vendor-neutral instructions: any coding agent reads this file. Claude Code additionally loads
`.claude/` (settings, hooks, subagents, commands) and the skills mirrored into `.claude/skills/`.

## Commands

```
dotnet test                                                    # 39 tests, ~5s warm
dotnet build
dotnet run --project src/Alcidion.Api --launch-profile http     # http://localhost:5025
dotnet format Alcidion.sln --include <file>                     # whitespace/style, ~9s
dotnet publish src/Alcidion.Api -c Release -o publish           # IIS, emits web.config
```

OpenAPI at `GET /openapi/v1.json` (Development only). Health at `GET /health`.

## Layout

```
src/Alcidion.Shared               IDomainEvent, IEventBus, IEventHandler<T>, Result<T>, IClock
src/Alcidion.Patients.Contracts   the only thing another context may reference
src/Alcidion.Patients             Patient aggregate, IPatientRepository, PatientService
src/Alcidion.Admissions           Admission aggregate, IKnownPatients read model, AdmissionService
src/Alcidion.Api                  controllers, JWT auth, CorrelationIdMiddleware, OpenTelemetry, [Audited]
tests/Alcidion.Domain.Tests       per-domain units, cross-domain event flow through real DI
tests/Alcidion.Api.Tests          integration via WebApplicationFactory
```

## Rules that are not negotiable

1. **Test first.** A change starts as a failing test named for the behaviour
   (`Register_with_whitespace_padded_duplicate_mrn_is_a_conflict`). A bug fix starts as a test that
   reproduces the bug. Never weaken or skip a test to get green.
2. **Invariants live at the write.** `TryAddAsync` / `TryAddActiveAsync` are the single enforcement
   points for "one patient per MRN" and "one active admission per patient". Never check-then-insert.
   `Admission.Discharge` takes check and write under one lock.
3. **Contexts talk through events.** `Alcidion.Admissions` may reference `Alcidion.Patients.Contracts`
   and nothing else from Patients. Need data from another context? Handle its event, keep your own
   read model.
4. **Expected failures are `Result<T>`.** Exceptions are for bugs. Status codes are mapped in one
   place, `ApiController.FromError`.
5. **Controllers are thin.** HTTP to command, `Result<T>` to status code. No domain logic.
6. **Time comes from `IClock`**, never `DateTime.Now` or `DateTime.UtcNow` in domain code.
7. **Every request carries a correlation id** through a logging scope; the console formatter keeps
   `IncludeScopes = true`. Do not pass correlation ids as parameters.
8. **Writes that change clinical state** get `[Authorize(Policy = Policies.Clinician)]` and
   `[Audited("domain.action")]`, with both the allowed and forbidden role asserted in `AuthTests`.
9. **Middleware order in `Program.cs` is load-bearing.** Adding middleware means justifying its
   position.
10. **Never commit a real secret.** `Jwt:Secret` in `appsettings.json` is a dev key; production values
    come from environment variables (`Jwt__Secret`, `Cors__Origins__0`).

## Verification before reporting done

`dotnet test` green with the output seen. For HTTP changes, the endpoint exercised. For logging or
tracing changes, an assertion that the output actually renders.

## Background

`../docs/lifecycle-and-hooks.md` walks a request through the API stage by stage and explains where
each concern belongs. Read it before moving a concern between layers.

## Skills

`skills/` holds the detailed playbooks (invariants, contexts and events, correlation and audit, TDD,
`Result<T>`). `skills/vendor/` holds third-party skills copied verbatim at a pinned commit - .NET Web
API, OpenTelemetry, test quality and diagnosis from `dotnet/skills`, design and diagnosis flows from
`mattpocock/skills` - refreshed with `node skills/vendor/update.mjs`, never edited in place. Where a
vendored skill and a first-party one disagree, the first-party one wins: it describes this code.
`.agents/skills/` is a generated mirror of both for agents that read this directory; it is gitignored.
Edit `skills/` and run `node .claude/hooks/sync-skills.mjs`.
