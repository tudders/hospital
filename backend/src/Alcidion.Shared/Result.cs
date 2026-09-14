namespace Alcidion.Shared;

/// <summary>
/// Explicit success/failure without exceptions for expected domain outcomes.
/// </summary>
public sealed record Error(string Code, string Message)
{
    /// <summary>
    /// The request field this failure is about, when there is one. Carried so the HTTP edge can key
    /// its <c>errors</c> dictionary by the name the caller actually sent, rather than leaving a
    /// client to read the field out of a sentence.
    /// </summary>
    public string? Field { get; init; }

    public static Error NotFound(string what, object id) => new("not_found", $"{what} '{id}' was not found.");

    /// <summary>
    /// The body is well formed and permitted, and names something that does not exist. Distinct
    /// from <see cref="NotFound"/>, which is about the URL: a 404 answering a POST reads as "no such
    /// endpoint", which sends a client looking for the wrong problem.
    /// </summary>
    public static Error UnprocessableReference(string field, string what, object id) =>
        new("unprocessable_reference", $"{what} '{id}' was not found.") { Field = field };
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
