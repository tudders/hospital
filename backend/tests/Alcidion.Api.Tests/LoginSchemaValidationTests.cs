using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Alcidion.Api.Tests;

/// <summary>
/// The edge contract for <c>POST /api/auth/login</c>. The schema asserts shape only: which
/// credentials are right stays a 401, and nothing here may turn a wrong password into a 400.
/// </summary>
public class LoginSchemaValidationTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<HttpResponseMessage> Post(object body) =>
        await api.CreateClient().PostAsJsonAsync("/api/auth/login", body);

    [Fact]
    public async Task A_missing_credential_names_the_field_as_the_caller_sent_it()
    {
        // Regression: the binder used to key these "Username" and "Password", in C# casing that no
        // caller ever typed, so the frontend could not match them to its inputs.
        var res = await Post(new { });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(
            ["password", "username"],
            (await res.FieldErrors()).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_username_is_refused_at_the_edge(string username)
    {
        var res = await Post(new { username, password = "nurse" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("username", (await res.FieldErrors()).Keys);
    }

    [Fact]
    public async Task A_credential_of_the_wrong_type_does_not_name_a_dotnet_type()
    {
        // Regression: this used to answer "The JSON value could not be converted to
        // Alcidion.Api.Controllers.LoginRequest. Path: $.username | LineNumber: 0 ...".
        var res = await Post(new { username = 123, password = true });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var messages = await res.Messages();
        Assert.DoesNotContain(messages, m => m.Contains("Alcidion", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("LineNumber", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_400_not_a_500()
    {
        var res = await api.CreateClient().PostAsync("/api/auth/login",
            new StringContent("not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_credentials_are_still_a_401_carrying_no_field_errors()
    {
        // A 400 here would tell a caller which half of the pair was the wrong one.
        var res = await Post(new { username = "nurse", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("errors", out _), $"a 401 must not name a field: {body}");
    }

    [Fact]
    public async Task An_unknown_user_answers_exactly_as_a_wrong_password_does()
    {
        // Shape validation must not have become a way to enumerate users.
        var unknown = await Post(new { username = "nobody", password = "nurse" });
        var wrong = await Post(new { username = "nurse", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        // Everything but the trace id, which is per-request by design.
        Assert.Equal(Told(await wrong.Content.ReadFromJsonAsync<JsonElement>()),
                     Told(await unknown.Content.ReadFromJsonAsync<JsonElement>()));
    }

    /// <summary>What a caller can read off a failure, trace id aside.</summary>
    private static string Told(JsonElement problem) => string.Join('|',
        problem.EnumerateObject()
            .Where(p => p.Name is not "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}"));

    [Fact]
    public async Task A_valid_login_still_issues_a_token()
    {
        var res = await Post(new { username = "nurse", password = "nurse" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task A_password_may_contain_anything_the_user_chose()
    {
        // The schema deliberately carries no password pattern. This is a wrong password, so a 401
        // is the right answer - a 400 would mean the schema had started refusing valid credentials.
        var res = await Post(new { username = "nurse", password = "  p@ss word\twith\u00a0spaces  " });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
