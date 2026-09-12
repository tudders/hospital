---
description: Add a vertical slice (use case) test-first, from failing test to endpoint, respecting the context boundaries
allowed-tools: Bash, Read, Write, Edit, Grep, Glob
---

Add the use case described in $ARGUMENTS as a vertical slice, test-first. If the description is too
vague to name a test, ask one question and stop; otherwise proceed without checking in.

Before writing anything, read `skills/invariants-at-the-write/SKILL.md`,
`skills/bounded-contexts-and-events/SKILL.md` and `skills/result-and-problem-details/SKILL.md`, and
decide which bounded context owns the use case. If both contexts appear to own it, the one that owns
the data owns the write; the other one learns by handling an event.

Work in this order, running `dotnet test` after each step:

1. **Red.** A failing test in `tests/Alcidion.Domain.Tests`, named for the behaviour in the repo's
   style (`Admitting_a_patient_twice_is_a_conflict`). Show it failing for the right reason.
2. **Aggregate.** Only the rules that hold regardless of caller. Invalid values that can never be
   valid throw `ArgumentException`; normalization goes on the aggregate and applies to both the stored
   value and the comparison key.
3. **Repository.** If the use case has a uniqueness or once-only rule, add a `TryX`-shaped method that
   returns `bool` and document it as the single enforcement point. Implement it so the key is claimed
   atomically. Never check-then-insert.
4. **Application service.** The use case: build, enforce at the write, log with the ambient correlation
   scope, publish the event after the write succeeds. Return `Result<T>`; map expected failures to
   `Error.Validation` / `Error.Conflict` / `Error.NotFound`.
5. **Event and handler**, if another context needs to know. Event record in `*.Contracts`, handler in
   the consuming context updating its own read model, registered in that context's
   `ServiceCollectionExtensions`, covered in `CrossDomainEventFlowTests`.
6. **Endpoint.** A thin action: command in, `Result<T>.Match` out, `FromError` for failures.
   `[Authorize(Policy = Policies.Clinician)]` and `[Audited("domain.action")]` on a write. Take and
   pass `CancellationToken`.
7. **Integration test** in `tests/Alcidion.Api.Tests`: the happy path, the conflict, and the forbidden
   role.
8. **Concurrency test** if step 3 added an enforcement point: sixteen real `Thread`s released by a
   `Barrier`, asserting one winner and one event.

Finish with `dotnet test` green and a short report: the files added, the rule each one enforces, and
anything still unverified.
