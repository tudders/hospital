---
name: invariants-at-the-write
description: Use when adding or reviewing any uniqueness rule, "only one active X" rule, or state transition that must happen once - registering a patient, admitting or discharging, or any new repository write. Covers why check-then-insert is a bug here and how to test it with real threads.
---

# Invariants live at the write

An invariant is enforced by the operation that commits it, never by a check that precedes it. Two
concurrent requests both pass a check; only one insert can win.

```csharp
// Wrong: both callers see "no existing patient", both insert.
if (await repo.GetByMrnAsync(cmd.Mrn) is not null) return Conflict();
await repo.AddAsync(patient);

// Right: the insert decides, and returning false is the conflict.
if (!await repo.TryAddAsync(patient, ct))
    return Result<Patient>.Fail(Error.Conflict($"A patient with MRN '{patient.Mrn}' already exists."));
```

## The enforcement points in this codebase

| Rule | Single enforcement point |
|---|---|
| One patient per MRN | `IPatientRepository.TryAddAsync` |
| One active admission per patient | `IAdmissionRepository.TryAddActiveAsync` |
| A discharge happens once | `Admission.Discharge`, check and write under one lock |

In memory these claim a key with `ConcurrentDictionary.TryAdd`. Against a relational database they
map a unique-index violation to the same `false`. The caller cannot tell the difference, which is
the point: swapping the implementation must not move the rule.

`ConcurrentDictionary` makes each *operation* atomic. It does not make a check-then-insert *pair*
atomic. If you find yourself reading before writing to decide whether the write is allowed, the rule
has escaped its enforcement point.

## Normalization must be symmetric

`Patient.NormalizeMrn` runs on the aggregate, and both the stored value and the uniqueness key go
through it. When normalization lived only in the constructor, `"REVIEW-1"` and `" review-1 "`
compared as different MRNs and stored as the same one. If you add a normalized field, normalize on
both sides of every comparison, and add the padded-duplicate test.

## How to add one

1. Write the failing test first, named for the behaviour:
   `Register_with_whitespace_padded_duplicate_mrn_is_a_conflict`.
2. Add a `TryX`-shaped method to the repository interface that returns `bool`, and document that it
   is the enforcement point.
3. Make the in-memory implementation claim the key atomically.
4. Map `false` to `Error.Conflict` in the application service; the controller turns that into 409 via
   `ApiController.FromError`.
5. Add a concurrency test (below).

## Concurrency tests use real threads

`Task.Run` is unsuitable: blocking on a barrier starves the thread pool, so the release is drip-fed
and the race never happens. Start sixteen real `Thread`s and release them with a `Barrier`, as the
existing tests in `tests/Alcidion.Domain.Tests` do. Assert that exactly one attempt succeeded and
exactly one event was published.

```
dotnet test --filter FullyQualifiedName~Concurren
```
