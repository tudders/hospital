using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Alcidion.Api.Tests;

/// <summary>
/// The published OpenAPI document is the reason for schema-first: it is what the frontend generates
/// from instead of re-implementing the rules by hand. Reflection over the generated structs
/// produces a document with no constraints and a <c>valueKind</c> member that is not part of the
/// contract, so what is asserted here is that the schema document itself is what gets published.
/// </summary>
public class ContractDocumentTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<JsonElement> Document()
    {
        // The document is only mapped in Development; the fixture runs as Testing.
        var client = api.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
    }

    private async Task<JsonElement> RegisterPatientSchema() =>
        (await Document()).GetProperty("components").GetProperty("schemas").GetProperty("RegisterPatientRequest");

    /// <summary>The body schema of a POST. An array body has no component of its own, so this reads
    /// it where it is published rather than out of <c>components.schemas</c>.</summary>
    private static JsonElement RequestBody(JsonElement document, string path) =>
        document.GetProperty("paths").GetProperty(path).GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    [Fact]
    public async Task The_published_contract_carries_the_constraints_the_schema_states()
    {
        var schema = await RegisterPatientSchema();
        var mrn = schema.GetProperty("properties").GetProperty("mrn");

        Assert.Equal("string", mrn.GetProperty("type").GetString());
        Assert.Equal(64, mrn.GetProperty("maxLength").GetInt32());
        Assert.Equal(@"^\s*[A-Za-z0-9][A-Za-z0-9._-]{0,63}\s*$", mrn.GetProperty("pattern").GetString());
        Assert.Equal("date", schema.GetProperty("properties").GetProperty("dateOfBirth").GetProperty("format").GetString());
    }

    [Fact]
    public async Task The_published_contract_names_every_required_field()
    {
        var required = (await RegisterPatientSchema()).GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).Order(StringComparer.Ordinal);

        Assert.Equal(["dateOfBirth", "familyName", "givenName", "mrn"], required);
    }

    [Fact]
    public async Task The_published_contract_leaks_nothing_from_the_generated_struct()
    {
        // Reflection over the Corvus struct offers a 'valueKind' member and one opaque component
        // schema per property. Neither is part of the wire contract.
        var schema = await RegisterPatientSchema();

        Assert.DoesNotContain("valueKind", schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("Entity", schema.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_array_body_publishes_the_contract_its_elements_have_to_meet()
    {
        // The telemetry batch is the first array-rooted body. Publishing only "type": array would
        // leave the frontend generating ClientEvent by hand again, which is the drift being fixed.
        var schema = RequestBody(await Document(), "/api/telemetry/events");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal(1, schema.GetProperty("minItems").GetInt32());

        var item = schema.GetProperty("items");
        Assert.Equal(
            ["at", "name", "sessionId"],
            item.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal("date-time", item.GetProperty("properties").GetProperty("at").GetProperty("format").GetString());
        Assert.Equal(64, item.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32());
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/admissions")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/telemetry/events")]
    public async Task Every_validated_endpoint_publishes_the_per_field_errors_dictionary(string path)
    {
        // A 400 documented as bare ProblemDetails hides the 'errors' dictionary that is the entire
        // point of validating at the edge: a frontend generated from that document cannot see it,
        // and goes back to guessing which input to mark.
        var document = await Document();
        var schema = document.GetProperty("paths").GetProperty(path).GetProperty("post")
            .GetProperty("responses").GetProperty("400").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");

        Assert.Equal("#/components/schemas/ValidationProblemDetails", schema.GetProperty("$ref").GetString());

        var errors = document.GetProperty("components").GetProperty("schemas")
            .GetProperty("ValidationProblemDetails").GetProperty("properties").GetProperty("errors");

        Assert.Equal("object", errors.GetProperty("type").GetString());
    }
}
