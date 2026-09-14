using System.Diagnostics;
using System.Net;
using Alcidion.Api.Observability;

namespace Alcidion.Api.Tests;

/// <summary>
/// <c>GET /api/patients?search=</c> puts a patient's name in the request URL, and the URL is copied
/// into telemetry that nobody configures with PHI in mind. Keeping the search a GET is a decision
/// (docs/adr/0004-patient-search-stays-a-get.md); keeping the name out of the span is the part of
/// that decision the API owes.
/// </summary>
public class QueryRedactionTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("?search=Lovelace", "?search=REDACTED")]
    [InlineData("search=Lovelace", "search=REDACTED")]
    [InlineData("?SEARCH=Lovelace", "?SEARCH=REDACTED")]
    [InlineData("?page=2&search=Ada%20Lovelace&sort=name", "?page=2&search=REDACTED&sort=name")]
    [InlineData("?search=", "?search=REDACTED")]
    public void A_sensitive_value_is_replaced_and_its_name_is_kept(string query, string expected)
    {
        // The name is what tells a search apart from a list in a trace. A redaction that took the
        // whole query would cost that, which is how redactions end up switched off again.
        Assert.Equal(expected, QueryRedaction.Redact(query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?page=2&sort=name")]
    [InlineData("?searching=true")]
    [InlineData("?flag")]
    public void A_query_carrying_nothing_sensitive_is_left_alone(string query)
    {
        Assert.Equal(query, QueryRedaction.Redact(query));
    }

    [Fact]
    public void A_null_query_is_the_empty_one()
    {
        Assert.Equal("", QueryRedaction.Redact(null));
    }

    [Fact]
    public async Task The_span_for_a_patient_search_does_not_carry_the_name_that_was_searched_for()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (stopped) stopped.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);

        // The listener is process-wide and the suite runs classes in parallel, so this request is
        // picked out by its own correlation id rather than by its path.
        var correlationId = Guid.NewGuid().ToString("N");
        var surname = $"Redact{correlationId[..8]}";
        var client = await api.ClientAs("nurse");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/patients?search={surname}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        Activity[] searches;
        lock (stopped) searches = [.. stopped.Where(a => a.GetTagItem("alcidion.correlation_id") as string == correlationId)];

        var span = Assert.Single(searches);
        Assert.Equal("?search=REDACTED", span.GetTagItem("url.query"));
        Assert.DoesNotContain(surname, string.Join('|', span.Tags.Select(t => t.Value?.ToString() ?? "")));
    }
}
