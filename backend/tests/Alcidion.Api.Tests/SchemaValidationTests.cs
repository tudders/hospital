using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// The edge contract for <c>POST /api/patients</c>, which is the precedent every other body will
/// copy. What is asserted here is the shape of the 400 as much as the fact of it: a per-field
/// <c>errors</c> dictionary keyed by the name the caller actually sent, with every failing field in
/// one response rather than the first one found.
/// </summary>
public class SchemaValidationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<HttpResponseMessage> Post(object body) =>
        await (await api.ClientAs("nurse")).PostAsJsonAsync("/api/patients", body);

    [Fact]
    public async Task A_missing_required_field_names_the_field()
    {
        var res = await Post(new { givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("mrn", (await res.FieldErrors()).Keys);
    }

    [Theory]
    [InlineData("   ")]                      // blank, not merely short
    [InlineData("has space")]                // outside the MRN character class
    [InlineData("-leading-punctuation")]     // must start alphanumeric
    public async Task A_malformed_mrn_is_refused_at_the_edge(string mrn)
    {
        var res = await Post(new { mrn, givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("mrn", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_blank_name_is_refused_at_the_edge()
    {
        // minLength alone accepts "   ", which then reaches the aggregate and comes back as an
        // error naming no field. The schema carries a non-blank pattern for exactly this.
        var res = await Post(new { mrn = "BLANK-1", givenName = "   ", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("givenName", (await res.FieldErrors()).Keys);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("1990-13-01")]
    [InlineData("1800-01-01")]   // before the 1875 floor
    public async Task A_date_of_birth_that_is_not_a_plausible_date_is_refused_at_the_edge(string dateOfBirth)
    {
        var res = await Post(new { mrn = "DOB-1", givenName = "Ada", familyName = "Lovelace", dateOfBirth });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("dateOfBirth", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task Every_failing_field_is_reported_in_one_response()
    {
        // A form with three bad fields must not need three round trips to fix.
        var res = await Post(new { mrn = "bad mrn", givenName = "", familyName = "", dateOfBirth = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = await res.FieldErrors();
        Assert.Equal(
            ["dateOfBirth", "familyName", "givenName", "mrn"],
            errors.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Error_messages_never_quote_the_schema_regular_expression()
    {
        // The pattern is part of the published contract, not something to read back at whoever
        // typed an MRN wrong.
        var res = await Post(new { mrn = "bad mrn", givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        var messages = await res.Messages();
        Assert.DoesNotContain(messages, m => m.Contains("[A-Za-z0-9]", StringComparison.Ordinal));
        Assert.Contains("The mrn field is not in the expected format.", messages);
    }

    [Fact]
    public async Task A_missing_field_is_reported_in_the_same_voice_as_a_malformed_one()
    {
        var res = await Post(new { givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Contains("The mrn field is required.", (await res.FieldErrors())["mrn"]);
    }

    [Fact]
    public async Task A_body_that_is_not_an_object_is_a_400_not_a_500()
    {
        var res = await (await api.ClientAs("nurse")).PostAsync("/api/patients",
            new StringContent("[]", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unrecognised_properties_are_ignored_rather_than_refused()
    {
        // The schema does not close the object: an older or newer client sending a field this
        // version does not know about must not be broken by it.
        var res = await Post(new { mrn = $"EXTRA-{Guid.NewGuid():N}"[..12], givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01", nickname = "Countess" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task A_whitespace_padded_mrn_still_reaches_the_aggregate()
    {
        // The MRN pattern tolerates padding deliberately. Losing that turns the 409 in
        // PatientFlowTests.Whitespace_padded_duplicate_mrn_is_409 into a 400.
        var mrn = $"PAD-{Guid.NewGuid():N}"[..10];
        var res = await Post(new { mrn = $"  {mrn}  ", givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(mrn.ToUpperInvariant(), created.GetProperty("mrn").GetString());
    }

    [Fact]
    public async Task A_future_date_of_birth_is_still_the_aggregates_rule()
    {
        // Deliberately not in the schema: a static document would bake in a build-time date. The
        // aggregate compares against the injected clock, so this 400 comes from the domain.
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");

        var res = await Post(new { mrn = "FUTURE-1", givenName = "Ada", familyName = "Lovelace", dateOfBirth = tomorrow });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_valid_body_is_still_accepted()
    {
        var res = await Post(new { mrn = "SCHEMA-1", givenName = "Ada", familyName = "Lovelace", dateOfBirth = "1990-01-01" });

        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"expected the schema to let a valid body through, got {res.StatusCode}");
    }
}
