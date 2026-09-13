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
        "validation" => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed", detail: error.Message),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Unexpected error", detail: error.Message),
    };
}
