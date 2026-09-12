using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Alcidion.Api.Tests;

public class CorrelationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Incoming_correlation_id_is_echoed()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "front-abc-123");

        var res = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("front-abc-123", res.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Missing_correlation_id_is_minted()
    {
        var res = await api.CreateClient().GetAsync("/health");

        var id = res.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Equal(32, id.Length);
    }

    [Fact]
    public async Task Ordinary_domain_logs_carry_the_correlation_id()
    {
        const string correlationId = "corr-domain-log-1";
        var client = await api.ClientAs("nurse");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var res = await client.PostAsJsonAsync("/api/patients",
            new { mrn = $"CORR-{Guid.NewGuid():N}"[..12], givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var tagged = api.Logs.Snapshot()
            .Where(l => l.Scopes.Any(s => s.Contains(correlationId, StringComparison.Ordinal)))
            .ToList();

        // The service and the cross-domain event handler both log without mentioning correlation
        // themselves; the scope is what joins them to the request.
        Assert.Contains(tagged, l => l.Category.EndsWith("PatientService", StringComparison.Ordinal));
        Assert.Contains(tagged, l => l.Category.EndsWith("PatientRegisteredHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void Console_output_is_configured_to_render_scopes()
    {
        // Carrying the scope is not enough: without this the console prints the message alone.
        var console = api.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var formatter = api.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Simple, console.FormatterName);
        Assert.True(formatter.IncludeScopes);
    }
}
