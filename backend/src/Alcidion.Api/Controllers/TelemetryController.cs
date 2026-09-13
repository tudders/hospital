using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

[Route("api/telemetry")]
[AllowAnonymous]
public sealed class TelemetryController(ILogger<TelemetryController> logger) : ApiController
{
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult Ingest([FromBody] ClientEventBatch body)
    {
        // No null-guard: a hole in the array fails the batch's item schema at the edge, which is
        // where "[null]" from a hand-built sendBeacon payload now becomes a 400 naming [0].
        var events = body.ToEvents();

        foreach (var e in events)
        {
            Telemetry.ClientEvents.Add(1, new KeyValuePair<string, object?>("event", e.Name));
            logger.LogInformation("CLIENT {Event} session={SessionId} seq={Seq} t={OffsetMs}ms at={At} props={Props}",
                e.Name, e.SessionId, e.Seq, e.T, e.At, e.Props);
        }

        return Accepted(new { received = events.Count, correlationId = HttpContext.CorrelationId() });
    }
}
