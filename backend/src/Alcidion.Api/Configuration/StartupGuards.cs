using System.Text;
using Alcidion.Api.Auth;

namespace Alcidion.Api.Configuration;

/// <summary>
/// What has to be true before this API is allowed to start. Development runs on demo scaffolding -
/// a committed signing key, four hard-coded users, in-memory repositories - and each of those is a
/// silent failure anywhere else rather than a loud one: an empty <c>Jwt:Secret</c> builds a
/// zero-length HMAC key instead of complaining, <see cref="DevTokenIssuer"/> answers admin/admin
/// with an administrator token, and an absent connection string starts an API that accepts patient
/// and admission writes and loses them on restart. Checked here so the deployment fails at startup
/// instead of behaving.
/// </summary>
public static class StartupGuards
{
    /// <summary>
    /// HS256 signs with whatever key it is handed; under 256 bits the token handler rejects it at
    /// the first login, which is far too late to read as a configuration mistake.
    /// </summary>
    private const int MinimumSecretBytes = 32;

    /// <summary>
    /// The environments allowed to run on demo scaffolding. Testing is one of them because the
    /// integration tests boot this same <c>Program</c> with no database and sign in as the demo
    /// users - a hermetic test run is the scaffolding doing its job.
    /// </summary>
    public static bool AllowsDemoScaffolding(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    /// <summary>Throws unless every guard passes, naming all of the failures at once.</summary>
    public static void Verify(IHostEnvironment environment, JwtOptions jwt, DemoUsers demoUsers, string? hospitalConnection)
    {
        var problems = Check(environment, jwt, demoUsers, hospitalConnection);
        if (problems.Count == 0) return;

        // One exception listing everything: fixing these one reboot at a time is the reason nobody
        // discovers the third one.
        throw new InvalidOperationException(
            $"The API cannot start in the {environment.EnvironmentName} environment:{Environment.NewLine}"
            + string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem)));
    }

    /// <summary>
    /// The guard failures, each naming the environment variable that settles it. Empty means start.
    /// </summary>
    public static IReadOnlyList<string> Check(IHostEnvironment environment, JwtOptions jwt, DemoUsers demoUsers, string? hospitalConnection)
    {
        if (AllowsDemoScaffolding(environment)) return [];

        var problems = new List<string>();

        if (Encoding.UTF8.GetByteCount(jwt.Secret) < MinimumSecretBytes)
        {
            problems.Add(
                $"Jwt:Secret is {Encoding.UTF8.GetByteCount(jwt.Secret)} bytes and HS256 needs at least "
                + $"{MinimumSecretBytes}. Set Jwt__Secret; an unset one signs every token with a zero-length key.");
        }

        if (demoUsers.Enabled)
        {
            problems.Add(
                "The demo token issuer is enabled, so admin/admin returns an administrator token. "
                + "Set Auth__AllowDemoUsers=false and put a real identity provider in front of this API.");
        }

        if (string.IsNullOrWhiteSpace(hospitalConnection))
        {
            problems.Add(
                "No hospital database is configured, so every domain would fall back to its in-memory "
                + "repository and lose all patient and admission writes on restart. Set ConnectionStrings__Hospital.");
        }

        return problems;
    }
}
