using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Alcidion.Api.Tests;

public class OpenApiMetadataTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<JsonElement> Document()
    {
        using var host = api.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = host.CreateClient();
        return await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
    }

    [Fact]
    public async Task Bearer_requirements_follow_authorization_including_anonymous_exceptions()
    {
        var document = await Document();
        var bearer = document.GetProperty("components"u8).GetProperty("securitySchemes"u8).GetProperty("Bearer"u8);
        Assert.Equal("http", bearer.GetProperty("type"u8).GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme"u8).GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat"u8).GetString());
        Assert.False(document.TryGetProperty("security"u8, out _));

        foreach (var path in document.GetProperty("paths"u8).EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            var anonymous = path.Name is "/api/auth/login" or "/api/telemetry/events" or "/health";
            var hasSecurity = operation.Value.TryGetProperty("security"u8, out var security) && security.GetArrayLength() > 0;
            Assert.Equal(!anonymous, hasSecurity);
            var responses = operation.Value.GetProperty("responses"u8);
            if (anonymous)
            {
                Assert.False(responses.TryGetProperty("403"u8, out _));
                if (path.Name != "/api/auth/login") Assert.False(responses.TryGetProperty("401"u8, out _));
                continue;
            }

            Assert.Empty(Assert.Single(security.EnumerateArray()).GetProperty("Bearer"u8).EnumerateArray());
            Assert.True(responses.TryGetProperty("401"u8, out _), path.Name);
            Assert.True(responses.TryGetProperty("403"u8, out _), path.Name);
        }
    }

    [Fact]
    public async Task Every_operation_has_a_unique_stable_name_and_useful_documentation()
    {
        var document = await Document();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.GetProperty("paths"u8).EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            var value = operation.Value;
            Assert.True(value.TryGetProperty("operationId"u8, out var id), $"{operation.Name} {path.Name}");
            Assert.False(string.IsNullOrWhiteSpace(id.GetString()));
            Assert.True(ids.Add(id.GetString()!), $"Duplicate operation ID: {id}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("summary"u8).GetString()));
            Assert.False(string.IsNullOrWhiteSpace(value.GetProperty("description"u8).GetString()));
        }

        Assert.Contains("GetPatient", ids);
        Assert.Contains("ChangeAdmission", ids);
    }

    [Fact]
    public async Task Responses_advertise_JSON_without_plain_text_or_XML_formatters()
    {
        var document = await Document();
        foreach (var path in document.GetProperty("paths"u8).EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        foreach (var response in operation.Value.GetProperty("responses"u8).EnumerateObject())
        {
            if (!response.Value.TryGetProperty("content"u8, out var content)) continue;
            Assert.All(content.EnumerateObject(), media =>
                Assert.True(media.Name is "application/json" or "application/problem+json", $"{path.Name}: {media.Name}"));
        }

        var patient = document.GetProperty("paths"u8).GetProperty("/api/patients/{id}"u8).GetProperty("get"u8)
            .GetProperty("responses"u8).GetProperty("200"u8).GetProperty("content"u8)
            .GetProperty("application/json"u8).GetProperty("schema"u8);
        Assert.Equal("#/components/schemas/PatientDto", patient.GetProperty("$ref"u8).GetString());
    }

    [Fact]
    public async Task Response_schemas_publish_XML_summaries_and_record_parameter_descriptions()
    {
        var schemas = (await Document()).GetProperty("components"u8).GetProperty("schemas"u8);
        foreach (var name in new[] { "PatientDto", "AdmissionDto", "WardDto", "MeResponse", "LoginResponse", "HospitalSnapshot", "HospitalBed", "TelemetryAcceptedResponse" })
        {
            Assert.True(schemas.GetProperty(name).TryGetProperty("description"u8, out var description), name);
            Assert.False(string.IsNullOrWhiteSpace(description.GetString()));
        }

        Assert.Contains("Allocatable right now", schemas.GetProperty("WardDto"u8).GetProperty("properties"u8)
            .GetProperty("freeBeds"u8).GetProperty("description"u8).GetString());
        Assert.Contains("ETag", schemas.GetProperty("PatientDto"u8).GetProperty("properties"u8)
            .GetProperty("version"u8).GetProperty("description"u8).GetString());
        Assert.Contains("requested", schemas.GetProperty("HospitalSnapshot"u8).GetProperty("properties"u8)
            .GetProperty("asOf"u8).GetProperty("description"u8).GetString());
    }
}
