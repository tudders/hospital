using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Alcidion.Api.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha256, HttpStatusCode.OK)]
    [InlineData(SecurityAlgorithms.HmacSha384, HttpStatusCode.Unauthorized)]
    [InlineData(SecurityAlgorithms.HmacSha512, HttpStatusCode.Unauthorized)]
    public async Task Only_HS256_tokens_are_accepted_even_when_other_signatures_are_valid(string algorithm, HttpStatusCode expected)
    {
        // A long key makes every tested HMAC valid; rejection must be about the algorithm,
        // not an undersized key or a signature that was invalid to begin with.
        using var host = api.WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", new string('s', 64)));
        using var client = host.CreateClient();
        var jwt = host.Services.GetRequiredService<JwtOptions>();
        var now = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            notBefore: now.AddMinutes(-1).UtcDateTime, expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: new SigningCredentials(jwt.SigningKey, algorithm));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(expected, response.StatusCode);
    }
}
