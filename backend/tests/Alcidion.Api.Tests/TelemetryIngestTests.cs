using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Alcidion.Api.Tests;

public class TelemetryIngestTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<HttpResponseMessage> Post(string body) =>
        await api.CreateClient().PostAsync("/api/telemetry/events",
            new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<Dictionary<string, string[]>> FieldErrors(HttpResponseMessage res)
    {
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("errors", out var errors),
            $"expected a per-field 'errors' dictionary, got: {body}");
        return errors.Deserialize<Dictionary<string, string[]>>()!;
    }

    [Fact]
    public async Task Recorded_session_events_keep_their_timeline_position()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        var client = api.CreateClient();

        // A recorded session arrives as an ordered batch: seq orders it, t places it in time.
        var res = await client.PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "session.start", sessionId, seq = 1, t = 0, at = DateTimeOffset.UtcNow, props = new { viewport = "1280x800" } },
            new { name = "ui.click", sessionId, seq = 2, t = 1420, at = DateTimeOffset.UtcNow, props = new { viewport = "1280x800" } },
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var lines = api.Logs.Snapshot().Where(l => l.Message.Contains(sessionId, StringComparison.Ordinal)).ToList();
        Assert.Contains(lines, l => l.Message.Contains("CLIENT session.start", StringComparison.Ordinal) && l.Message.Contains("seq=1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Message.Contains("CLIENT ui.click", StringComparison.Ordinal) && l.Message.Contains("t=1420ms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_event_payload_reaches_the_log_line_as_the_caller_sent_it()
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";

        var res = await api.CreateClient().PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "ui.click", sessionId, at = DateTimeOffset.UtcNow, props = new { target = "admit", viewport = (string?)null } },
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Contains(api.Logs.Snapshot(),
            l => l.Message.Contains(sessionId, StringComparison.Ordinal) && l.Message.Contains("\"target\":\"admit\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_null_event_in_the_batch_is_rejected_naming_its_position()
    {
        // sendBeacon payloads are built by hand elsewhere; a hole in the array must not reach the
        // logging loop, which would dereference it and 500 on an anonymous endpoint. The batch
        // schema refuses it at the edge, so the controller carries no null-guard of its own.
        var res = await Post("[null]");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("[0]", (await FieldErrors(res)).Keys);
    }

    [Fact]
    public async Task A_malformed_event_is_reported_against_its_index_and_field()
    {
        // The pointer the schema reports is /1/name; the key has to be the one ModelState and the
        // frontend use, or the caller is told which rule broke without being told where.
        var res = await Post("""
            [{"name":"page_view","sessionId":"s1","at":"2026-01-01T00:00:00Z"},
             {"name":"NOT A NAME","sessionId":"s1","at":"2026-01-01T00:00:00Z"}]
            """);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = await FieldErrors(res);
        Assert.Contains("[1].name", errors.Keys);
        Assert.Contains("The [1].name field is not in the expected format.", errors["[1].name"]);

        // The element also fails its own item subschema, which says only "did not match". That is
        // noise next to the message above, so it is held back.
        Assert.DoesNotContain("[1]", errors.Keys);
    }

    [Fact]
    public async Task An_event_missing_a_required_field_names_the_field_under_its_index()
    {
        var res = await Post("""[{"sessionId":"s1","at":"2026-01-01T00:00:00Z"}]""");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("The [0].name field is required.", (await FieldErrors(res))["[0].name"]);
    }

    [Fact]
    public async Task An_empty_batch_is_refused()
    {
        // The client never sends one - flush() returns early on an empty queue - so a batch with no
        // events is a caller bug worth naming rather than an accepted no-op.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("[]")).StatusCode);
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

    [Fact]
    public async Task An_unrecognised_prop_is_carried_rather_than_refused()
    {
        // props is open by contract: a client that records a field this version does not know
        // about must not have its whole batch rejected.
        var res = await api.CreateClient().PostAsJsonAsync("/api/telemetry/events", new[]
        {
            new { name = "page_view", sessionId = "open-1", at = DateTimeOffset.UtcNow, props = new { somethingNew = 42 } },
        });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }
}
