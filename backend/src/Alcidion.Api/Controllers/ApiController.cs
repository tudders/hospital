using Alcidion.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <summary>Base controller: one place to map domain <see cref="Error"/>s onto HTTP problem details.</summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected ActionResult FromError(Error error) => error.Code switch
    {
        "hospital_unavailable" => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Hospital data unavailable", detail: error.Message),
        "not_found" => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: error.Message),
        "conflict" => Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: error.Message),
        // The request is well formed and permitted; it was decided against a version that has since
        // moved. Re-reading is the whole of the fix, which is what 412 tells a client and 409 does not.
        "precondition_failed" => Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Precondition failed", detail: error.Message),
        "validation" => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: error.Message),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error", detail: error.Message),
    };
}
