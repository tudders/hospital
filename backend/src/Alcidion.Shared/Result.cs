namespace Alcidion.Shared;

/// <summary>
/// Explicit success/failure without exceptions for expected domain outcomes.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static Error NotFound(string what, object id) => new("not_found", $"{what} '{id}' was not found.");
    public static Error Validation(string message) => new("validation", message);
    public static Error Conflict(string message) => new("conflict", message);

    /// <summary>
    /// The caller named the version it expected and the stored one has moved past it. Distinct from
    /// a conflict: nothing about the request is wrong, it was simply decided against a state that no
    /// longer holds, and re-reading is the whole of the fix.
    /// </summary>
    public static Error PreconditionFailed(string message) => new("precondition_failed", message);
}

public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    private Result(T? value, Error? error) { Value = value; Error = error; }

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(Error error) => new(default, error);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail) =>
        IsSuccess ? ok(Value!) : fail(Error!);
}
