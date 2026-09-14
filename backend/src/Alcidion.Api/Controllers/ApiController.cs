using Alcidion.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Net.Http.Headers;

namespace Alcidion.Api.Controllers;

/// <summary>Base controller: one place to map domain <see cref="Error"/>s onto HTTP problem details.</summary>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class ApiController : ControllerBase
{
    protected ActionResult FromError(Error error) => error.Code switch
    {
        "hospital_unavailable" => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Hospital data unavailable", detail: error.Message),
        "not_found" => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: error.Message),
        // The URL resolved and the body parsed; what the body names does not exist. 404 would say
        // the endpoint is wrong, which is the one thing this is not - so the field is named instead,
        // in the same errors dictionary a schema failure uses.
        "unprocessable_reference" => ValidationProblem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Unprocessable entity",
            detail: error.Message,
            modelStateDictionary: ModelStateFor(error)),
        "conflict" => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: error.Message),
        // The request is well formed and permitted; it was decided against a version that has since
        // moved. Re-reading is the whole of the fix, which is what 412 tells a client and 409 does not.
        "precondition_failed" => Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition failed", detail: error.Message),
        "validation" => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: error.Message),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error", detail: error.Message),
    };

    /// <summary>
    /// The version this request is conditional on, or <see langword="null"/> when the caller sent no
    /// usable one. <c>If-Match: *</c> counts as none on purpose: it asserts only that the resource
    /// exists, which is not the question a state change has to be right about. A weak tag counts as
    /// none for the same reason - it says two representations are equivalent, not identical.
    /// </summary>
    protected long? ExpectedVersion() =>
        EntityTagHeaderValue.TryParseList(Request.Headers.IfMatch, out var tags)
        && tags is [{ IsWeak: false, Tag.Length: > 2 } only]
        && long.TryParse(only.Tag.Value.AsSpan()[1..^1], out var version)
            ? version
            : null;

    /// <summary>
    /// Refuses a change that named no version. 428 rather than 400: the request is not malformed,
    /// it is missing the precondition that makes it safe, and 428 is what tells a client to re-read
    /// and send the tag rather than to fix its body.
    /// </summary>
    protected ActionResult PreconditionRequired(string what) => Problem(
        statusCode: StatusCodes.Status428PreconditionRequired,
        title: "Precondition required",
        detail: $"Send If-Match with the ETag of the {what} you read. Without it a change someone "
              + "else made in between would be overwritten rather than refused.");

    /// <summary>
    /// Publishes a version as the response's ETag, so the next change can be taken against the state
    /// the caller actually saw. Weak would be wrong: the tag has to compare equal only to the exact
    /// version, which is what a strong comparison in <c>If-Match</c> means.
    /// </summary>
    protected void PublishVersion(long version) => Response.Headers.ETag = $"\"{version}\"";

    /// <summary>
    /// The error as a one-entry model state, keyed by the field it names. Built here rather than
    /// mutating the action's own <see cref="ControllerBase.ModelState"/>, which has already been
    /// validated and reported clean by the time an application error comes back.
    /// </summary>
    private static ModelStateDictionary ModelStateFor(Error error)
    {
        var state = new ModelStateDictionary();
        state.AddModelError(error.Field ?? "", error.Message);
        return state;
    }
}
