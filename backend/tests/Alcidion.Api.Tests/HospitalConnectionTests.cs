using Alcidion.Api.Hospital;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Alcidion.Api.Tests;

/// <summary>
/// One resolver decides where every domain and the occupancy reader connect, so what it does to a
/// connection string matters to all of them. The TLS default in particular has to stay a
/// development affordance: silently trusting an unverified certificate in production would take a
/// deployment's transport security away without anyone choosing it.
/// </summary>
public class HospitalConnectionTests
{
    private const string Plain = "Server=db;Database=alcidion;User Id=u;Password=p;";

    private static string? Resolve(string? connectionString, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null
                ? []
                : new Dictionary<string, string?> { ["ConnectionStrings:Hospital"] = connectionString })
            .Build();
        return HospitalConnection.Resolve(configuration, new StubEnvironment(environment));
    }

    private static bool Trusts(string? connectionString) =>
        new SqlConnectionStringBuilder(connectionString).TrustServerCertificate;

    [Fact]
    public void No_connection_string_means_no_database() =>
        Assert.Null(Resolve(null, Environments.Production));

    [Fact]
    public void A_blank_connection_string_is_the_same_as_none() =>
        Assert.Null(Resolve("   ", Environments.Development));

    [Fact]
    public void Development_trusts_the_local_server_certificate() =>
        Assert.True(Trusts(Resolve(Plain, Environments.Development)));

    [Fact]
    public void Production_is_left_exactly_as_configured() =>
        Assert.Equal(Plain, Resolve(Plain, Environments.Production));

    [Theory]
    [InlineData("TrustServerCertificate=False;")]
    [InlineData("Encrypt=True;")]
    public void A_connection_string_that_states_its_own_TLS_keeps_it(string tls)
    {
        // Saying "do not trust this certificate" has to survive, or the development default would
        // quietly overrule the one place someone said what they wanted.
        Assert.False(Trusts(Resolve(Plain + tls, Environments.Development)));
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
