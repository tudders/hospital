using System.Net;
using System.Net.Http.Json;

namespace Alcidion.Api.Tests;

public class AuthTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Anonymous_cannot_list_patients()
    {
        var res = await api.CreateClient().GetAsync("/api/patients");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Bad_credentials_are_rejected()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "nurse", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_read_but_not_register()
    {
        var client = await api.ClientAs("viewer");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/patients")).StatusCode);

        var res = await client.PostAsJsonAsync("/api/patients", new { mrn = "V1", givenName = "A", familyName = "B", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Me_returns_roles()
    {
        var client = await api.ClientAs("admin");
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        Assert.Equal("admin", me!.Name);
        Assert.Contains("admin", me.Roles);
    }

    private sealed record MeBody(string Name, string[] Roles);
}
