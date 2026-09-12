---
name: result-and-problem-details
description: Use when adding a controller action or an application-service method, deciding between an exception and a failure value, choosing the HTTP status code for a domain outcome, or adding authorization to a write. Covers Result<T>, the Error codes, ApiController.FromError and what the frontend expects.
---

# Expected failures are values; bugs are exceptions

An outcome the domain anticipates - not found, conflict, invalid input - is a `Result<T>`, never an
exception. Exceptions are for the unanticipated, and they surface as problem details through
`UseExceptionHandler`.

```csharp
public readonly record struct Result<T>   // Value, Error, IsSuccess, Match
public sealed record Error(string Code, string Message)
    // Error.NotFound(what, id) | Error.Validation(message) | Error.Conflict(message)
```

## Layer responsibilities

- **Aggregate** - rules that hold regardless of caller. Throws `ArgumentException` for a value that
  can never be valid (an empty MRN); the application service turns that into `Error.Validation`.
- **Application service** - the use case: build the aggregate, enforce the invariant at the write,
  log, publish. Returns `Result<T>`.
- **Controller** - translates HTTP into a command and a `Result<T>` back into a status code. Nothing
  else: no domain logic, no repository access, no event publishing.

```csharp
var result = await service.RegisterAsync(cmd, ct);
return result.Match<ActionResult>(
    patient => CreatedAtAction(nameof(Get), new { id = patient.Id }, patient),
    FromError);
```

## Status-code mapping lives in one place

`ApiController.FromError`:

| Error code | Status |
|---|---|
| `validation` | 400 |
| `not_found` | 404 |
| `conflict` | 409 |
| anything else | 500 |

A new error code means a case there plus a test in `Alcidion.Api.Tests` asserting the status. Do not
return a bare `StatusCode(...)` from an action: the frontend parses RFC 9457 problem details and shows
`detail` together with the correlation id in its error alert.

## Auth on a new action

Reads require any authenticated user. Writes require the `Clinician` policy (`clinician` or `admin`
role). Put `[Authorize(Policy = Policies.Clinician)]` and `[Audited("domain.action")]` on a write
together, and cover both the permitted and the forbidden role in `AuthTests` - an unasserted 403 is a
200 waiting to happen.
