using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Alcidion.Api.Tests;

public class HospitalOccupancyTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Anonymous_cannot_read_hospital_occupancy()
    {
        var response = await api.CreateClient().GetAsync("/api/hospital-occupancy");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unconfigured_database_returns_unavailable_with_correlation_and_no_credentials()
    {
        using var host = api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Hospital"] = "",
                ["SQL_SERVE_CONNECTION_STRING"] = ""
            })));
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "viewer", password = "viewer" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "hospital-read-check");
        var response = await client.GetAsync("/api/hospital-occupancy");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("hospital-read-check", response.Headers.GetValues("X-Correlation-Id").Single());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hospital data unavailable", problem.GetProperty("title").GetString());
        Assert.DoesNotContain("Password", problem.GetRawText());
    }

    [Fact]
    public async Task Invalid_snapshot_time_returns_bad_request()
    {
        var client = await api.ClientAs("viewer");
        var response = await client.GetAsync("/api/hospital-occupancy?at=not-a-time");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Future_snapshot_time_is_rejected_before_accessing_the_database()
    {
        var client = await api.ClientAs("viewer");
        var response = await client.GetAsync("/api/hospital-occupancy?at=9999-01-01T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("past", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
