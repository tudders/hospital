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
    public IActionResult Ingest([FromBody] ClientEvent[] events)
    {
        foreach (var e in events)
        {
            Telemetry.ClientEvents.Add(1, new KeyValuePair<string, object?>("event", e.Name));
            logger.LogInformation("CLIENT {Event} session={SessionId} seq={Seq} t={OffsetMs}ms at={At} props={@Props}",
                e.Name, e.SessionId, e.Seq, e.T, e.At, e.Props);
        }
        return Accepted(new { received = events.Length, correlationId = HttpContext.CorrelationId() });
    }
}
