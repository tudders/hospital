using Alcidion.Api.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <summary>
/// A single frontend event. The browser batches these and posts them with a correlation id.
/// <paramref name="Seq"/> orders the session's events regardless of the order batches arrive in,
/// and <paramref name="T"/> is the millisecond offset from session start, so a session can be
/// replayed at the pace the user experienced.
/// </summary>
public sealed record ClientEvent(string Name, string SessionId, DateTimeOffset At, long Seq = 0, long T = 0, Dictionary<string, object?>? Props = null);

[Route("api/telemetry")]
[AllowAnonymous]
public sealed class TelemetryController(ILogger<TelemetryController> logger) : ApiController
{
    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Ingest([FromBody] ClientEvent[] events)
    {
        // System.Text.Json puts nulls in the array whatever the element type says it holds, and the
        // loop below dereferences them: without this guard an anonymous caller turns "[null]" into a
        // 500. Rejecting the batch matches the 400 model binding already gives an event missing a name.
        var nullAt = Array.FindIndex(events, e => e is null);
        if (nullAt >= 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Validation failed",
                detail: $"Event at index {nullAt} is null.");

        foreach (var e in events)
        {
            Telemetry.ClientEvents.Add(1, new KeyValuePair<string, object?>("event", e.Name));
            logger.LogInformation("CLIENT {Event} session={SessionId} seq={Seq} t={OffsetMs}ms at={At} props={@Props}",
                e.Name, e.SessionId, e.Seq, e.T, e.At, e.Props);
        }
        return Accepted(new { received = events.Length, correlationId = HttpContext.CorrelationId() });
    }
}
