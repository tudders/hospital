---
name: tdd-and-verification
description: Use before writing any backend production code, when fixing a bug, or before reporting work complete. Covers the failing-test-first loop, test naming, which test project a test belongs in, how to test races and DI wiring, and what counts as verification.
---

# Red first, then green

Every change starts as a failing test that names the behaviour. A bug fix starts as a test that
reproduces the bug - if you cannot make it fail, you have not found the bug yet.

```
dotnet test                                                    # 39 tests, ~5s warm
dotnet test --filter FullyQualifiedName~Concurren               # one slice
dotnet build                                                   # compile only
dotnet run --project src/Alcidion.Api --launch-profile http     # http://localhost:5025
```

## Where a test belongs

| Project | For |
|---|---|
| `tests/Alcidion.Domain.Tests` | Aggregates, application services, cross-domain event flow through the real DI container |
| `tests/Alcidion.Api.Tests` | HTTP behaviour via `WebApplicationFactory`: auth, correlation, status codes, end-to-end flow |

Prefer the domain project. Reach for `Alcidion.Api.Tests` when the thing under test *is* the HTTP
edge: a policy, a header, a status-code mapping, a problem-details body.

## Naming

Sentences, snake_case, stating the rule:

```
Register_with_whitespace_padded_duplicate_mrn_is_a_conflict
Ordinary_domain_logs_carry_the_correlation_id
Discharging_twice_publishes_one_event
```

A name like `Test1` or `RegisterAsync_Works` is not done.

## Test real wiring, not mocks, for wiring

`TestServices.BuildRealWiring` builds the container with `ValidateOnBuild` and `ValidateScopes`.
Cross-context behaviour is tested through that container, so a captive dependency or a missing
registration fails here rather than in production. A new registration gets proved there.

## Races

Use real `Thread`s released by a `Barrier`, sixteen of them. `Task.Run` starves the thread pool when
the bodies block on the barrier, so the release is drip-fed and the race never happens. Assert
exactly one winner and exactly one published event.

## What counts as verified

- `dotnet test` green, with the output seen, not assumed.
- Anything touching HTTP: the request exercised, not just the unit beneath it.
- Anything touching logging or tracing: an assertion that the output actually renders.

A green build alone is not verification, and a test is never weakened or skipped to reach green.
