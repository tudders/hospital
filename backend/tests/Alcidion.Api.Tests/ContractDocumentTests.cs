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

    /// <summary>Follows a <c>$ref</c> into <c>components.schemas</c>; anything else is already the
    /// schema.</summary>
    private static JsonElement Resolve(JsonElement document, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? document.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/')[^1])
            : schema;

    /// <summary>The body schema of a POST. An array body has no component of its own, so this reads
    /// it where it is published rather than out of <c>components.schemas</c>.</summary>
    private static JsonElement RequestBody(JsonElement document, string path) =>
        document.GetProperty("paths").GetProperty(path).GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    /// <summary>
    /// OpenAPI.NET 2.x may publish primitive numeric schemas as an anyOf containing the primitive
    /// branch and its JSON string-compatible branch. Select the branch whose type is under test so
    /// this assertion remains about the contract rather than the serializer representation.
    /// </summary>
    private static JsonElement PrimitiveBranch(JsonElement schema, string type) =>
        schema.TryGetProperty("type", out var direct) && direct.ValueKind is JsonValueKind.String
            ? schema
            : schema.GetProperty("anyOf").EnumerateArray()
                .Single(branch => branch.GetProperty("type").GetString() == type);

    [Fact]
    public async Task Telemetry_publishes_its_limits_and_accepted_response()
    {
        var document = await Document();
        var properties = RequestBody(document, "/api/telemetry/events").GetProperty("items"u8).GetProperty("properties"u8);
        Assert.Equal(long.MaxValue, properties.GetProperty("seq"u8).GetProperty("maximum"u8).GetInt64());
        Assert.Equal(long.MaxValue, properties.GetProperty("t"u8).GetProperty("maximum"u8).GetInt64());
        var props = properties.GetProperty("props"u8);
        Assert.Equal(32, props.GetProperty("maxProperties"u8).GetInt32());
        var value = props.GetProperty("additionalProperties"u8);
        Assert.Equal(1024, value.GetProperty("maxLength"u8).GetInt32());
        Assert.All(value.GetProperty("anyOf"u8).EnumerateArray(), s => Assert.True(s.GetProperty("nullable"u8).GetBoolean()));
        Assert.Equal(["boolean", "number", "string"], value.GetProperty("anyOf"u8).EnumerateArray()
            .Select(s => s.GetProperty("type"u8).GetString()).Order(StringComparer.Ordinal));
        var response = document.GetProperty("paths"u8).GetProperty("/api/telemetry/events"u8).GetProperty("post"u8)
            .GetProperty("responses"u8).GetProperty("202"u8).GetProperty("content"u8).GetProperty("application/json"u8).GetProperty("schema"u8);
        var fields = Resolve(document, response).GetProperty("properties"u8);
        Assert.Equal("integer", PrimitiveBranch(fields.GetProperty("received"u8), "integer").GetProperty("type"u8).GetString());
        Assert.Equal("string", fields.GetProperty("correlationId"u8).GetProperty("type"u8).GetString());
    }

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

    [Fact]
    public async Task The_published_admission_contract_carries_its_constraints()
    {
        var schema = Resolve(await Document(), RequestBody(await Document(), "/api/admissions"));

        Assert.Equal(
            ["patientId", "ward"],
            schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order(StringComparer.Ordinal));
        Assert.Equal("uuid", schema.GetProperty("properties").GetProperty("patientId").GetProperty("format").GetString());
        Assert.Equal(200, schema.GetProperty("properties").GetProperty("ward").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public async Task The_published_login_contract_describes_the_username_and_not_the_password()
    {
        // The username's bounds are useful to a caller. A published password pattern would describe
        // the credential format to everyone who can read the document, so the schema carries none
        // and this asserts it stays that way.
        var schema = Resolve(await Document(), RequestBody(await Document(), "/api/auth/login"));
        var properties = schema.GetProperty("properties");

        Assert.Equal(64, properties.GetProperty("username").GetProperty("maxLength").GetInt32());
        Assert.False(properties.GetProperty("password").TryGetProperty("pattern", out _),
            "the login schema must not publish a password pattern");
    }

    [Fact]
    public async Task Every_published_request_body_comes_from_a_schema_document()
    {
        // The document-side twin of RequestBodyContractTests: a body reflected off a C# type has no
        // title, because a title is something only the schema document gives it. This is what
        // catches an endpoint whose rules are real in code but absent from what clients generate.
        var document = await Document();
        var untitled = new List<string>();

        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("requestBody", out var body)) continue;
                if (!body.GetProperty("content").TryGetProperty("application/json", out var json)) continue;

                var schema = Resolve(document, json.GetProperty("schema"));
                if (!schema.TryGetProperty("title", out _)) untitled.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        Assert.True(untitled.Count == 0,
            "These request bodies are published without the schema document that defines them, so a " +
            "generated client cannot see their rules:" + Environment.NewLine + string.Join(Environment.NewLine, untitled));
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
