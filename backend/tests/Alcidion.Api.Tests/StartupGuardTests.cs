using System.Net;
using System.Net.Http.Json;
using Alcidion.Api.Auth;
using Alcidion.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Alcidion.Api.Tests;

/// <summary>
/// The demo scaffolding - a committed signing key, hard-coded users, in-memory repositories - is
/// what makes development and these tests work without configuration, and all three are silent
/// when they are wrong. These assert that the silence ends at the environment boundary.
/// </summary>
public class StartupGuardTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string RealSecret = "a-real-signing-key-that-is-long-enough-32";
    private const string RealConnection = "Server=db;Database=alcidion;User Id=u;Password=p;";

    private static IReadOnlyList<string> Check(
        string environment,
        string secret = RealSecret,
        bool demoUsers = false,
        string? connection = RealConnection) =>
        StartupGuards.Check(
            new StubEnvironment(environment),
            new JwtOptions { Secret = secret },
            new DemoUsers(demoUsers),
            connection);

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Development_and_testing_run_on_the_scaffolding_by_design(string environment) =>
        Assert.Empty(Check(environment, secret: "", demoUsers: true, connection: null));

    [Fact]
    public void A_fully_configured_production_deployment_starts() =>
        Assert.Empty(Check(Environments.Production));

    [Fact]
    public void An_unset_signing_key_is_refused_rather_than_signing_with_nothing()
    {
        // JwtOptions.Secret defaults to "", and SymmetricSecurityKey takes a zero-length key
        // without complaint - the reason this has to be caught by name.
        var problem = Assert.Single(Check(Environments.Production, secret: ""));
        Assert.Contains("Jwt__Secret", problem);
    }

    [Fact]
    public void A_signing_key_too_short_for_HS256_is_refused_at_startup_not_at_the_first_login()
    {
        var problem = Assert.Single(Check(Environments.Production, secret: new string('k', 31)));
        Assert.Contains("31 bytes", problem);
    }

    [Fact]
    public void The_demo_users_are_refused_outside_development()
    {
        var problem = Assert.Single(Check(Environments.Production, demoUsers: true));
        Assert.Contains("Auth__AllowDemoUsers", problem);
    }

    [Fact]
    public void A_missing_database_is_refused_rather_than_served_from_memory()
    {
        var problem = Assert.Single(Check(Environments.Production, connection: null));
        Assert.Contains("ConnectionStrings__Hospital", problem);
    }

    [Fact]
    public void A_blank_connection_string_counts_as_missing() =>
        Assert.Single(Check(Environments.Staging, connection: "   "));

    [Fact]
    public void Every_failure_is_named_at_once()
    {
        // Three deployments that each got one step further is the failure mode a single-check
        // guard produces.
        Assert.Equal(3, Check(Environments.Production, secret: "", demoUsers: true, connection: null).Count);
    }

    [Fact]
    public void Verify_names_the_environment_it_refused_to_start_in()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => StartupGuards.Verify(
            new StubEnvironment(Environments.Production), new JwtOptions(), new DemoUsers(true), null));

        Assert.Contains(Environments.Production, thrown.Message);
    }

    [Fact]
    public void The_API_does_not_start_in_production_on_demo_scaffolding()
    {
        // The whole Program, not the guard in isolation: a guard that is never called reads
        // exactly like one that passes.
        using var api = new UnconfiguredProductionApi();

        var thrown = Assert.ThrowsAny<Exception>(() => api.CreateClient());

        var message = Flatten(thrown);
        Assert.Contains("Jwt__Secret", message);
        Assert.Contains("Auth__AllowDemoUsers", message);
        Assert.Contains("ConnectionStrings__Hospital", message);
    }

    [Fact]
    public async Task Login_says_there_is_no_identity_provider_rather_than_rejecting_the_password()
    {
        using var withoutDemoUsers = api.WithWebHostBuilder(b => b.UseSetting("Auth:AllowDemoUsers", "false"));

        var res = await withoutDemoUsers.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });

        Assert.Equal(HttpStatusCode.NotImplemented, res.StatusCode);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is { } inner ? exception.Message + Environment.NewLine + Flatten(inner) : exception.Message;

    /// <summary>
    /// Production with nothing configured - which is what the repository ships: no signing key
    /// outside appsettings.Development.json, no connection string, and demo users left on.
    /// </summary>
    private sealed class UnconfiguredProductionApi : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment(Environments.Production);
    }

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
