using System.Net;
using System.Net.Http.Json;

namespace Alcidion.Api.Tests;

public class TelemetryIngestTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Recorded_session_events_keep_their_timeline_position()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        var client = api.CreateClient();

        // A recorded session arrives as an ordered batch: seq orders it, t places it in time.
        var res = await client.PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "session.start", sessionId, seq = 1, t = 0, at = DateTimeOffset.UtcNow, props = new { viewport = "1280x800" } },
            new { name = "ui.click", sessionId, seq = 2, t = 1420, at = DateTimeOffset.UtcNow, props = new { viewport = (string?)null } },
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var lines = api.Logs.Snapshot().Where(l => l.Message.Contains(sessionId, StringComparison.Ordinal)).ToList();
        Assert.Contains(lines, l => l.Message.Contains("CLIENT session.start", StringComparison.Ordinal) && l.Message.Contains("seq=1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Message.Contains("CLIENT ui.click", StringComparison.Ordinal) && l.Message.Contains("t=1420ms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Events_without_timeline_fields_are_still_accepted()
    {
        // Older clients, and sendBeacon payloads built elsewhere, must not 400.
        var res = await api.CreateClient().PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "page_view", sessionId = "legacy-1", at = DateTimeOffset.UtcNow },
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }
}
