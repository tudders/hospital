using Microsoft.Data.SqlClient;

namespace Alcidion.Sql.Tests;

/// <summary>
/// Whether this machine has a SQL Server these tests can build a throwaway database on, and the
/// reason they are skipped when it does not. The tests in this project exercise raw T-SQL, table
/// hints, triggers and <c>datetimeoffset</c> semantics, so no in-memory or SQLite provider can
/// stand in for the real server: without one, the honest outcome is a skip, not a green run.
/// </summary>
public static class SqlServerProbe
{
    /// <summary>Points the suite at a server; set it in CI. Any database in it is ignored.</summary>
    public const string EnvironmentVariable = "ALCIDION_TEST_SQL";

    private static readonly Lazy<Probe> Result = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Null when the suite can run; otherwise the reason it cannot, shown on every skip.</summary>
    public static string? SkipReason => Result.Value.Skip;

    /// <summary>A connection to <c>master</c> on the server the scratch database is created on.</summary>
    public static string MasterConnectionString =>
        Result.Value.Master ?? throw new InvalidOperationException(Result.Value.Skip);

    private readonly record struct Probe(string? Master, string? Skip);

    private static Probe Detect()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        string[] candidates = string.IsNullOrWhiteSpace(configured)
            ? ["Server=(local);Integrated Security=True", "Server=localhost;Integrated Security=True"]
            : [configured];

        string? lastFailure = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var master = ToMaster(candidate);
                using var connection = new SqlConnection(master);
                connection.Open();
                using var command = connection.CreateCommand();
                // Creating and dropping the scratch database is the whole point; without the right
                // to do it the suite would fail for a reason that says nothing about the code.
                command.CommandText = "SELECT IS_SRVROLEMEMBER('sysadmin') | IS_SRVROLEMEMBER('dbcreator')";
                if (Convert.ToInt32(command.ExecuteScalar()) == 1) return new Probe(master, null);
                lastFailure = "the login may not create databases";
            }
            catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
            {
                lastFailure = ex.Message.Split('\n')[0].Trim();
            }
        }

        return new Probe(null, $"No usable SQL Server: {lastFailure}. Set {EnvironmentVariable} to a server " +
            "whose login can create databases to run the SQL-backed tests.");
    }

    /// <summary>
    /// Connects to <c>master</c> whatever database the caller named, and keeps the timeouts short:
    /// a developer without a server should learn that in seconds, not wait out a default timeout
    /// once per candidate.
    /// </summary>
    private static string ToMaster(string connectionString) => new SqlConnectionStringBuilder(connectionString)
    {
        InitialCatalog = "master",
        TrustServerCertificate = true,
        ConnectTimeout = 5,
        CommandTimeout = 120,
        Pooling = false,
    }.ConnectionString;
}

/// <summary>A fact that runs only where <see cref="SqlServerProbe"/> found a server it can use.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute() => Skip = SqlServerProbe.SkipReason;
}

/// <summary>A theory that runs only where <see cref="SqlServerProbe"/> found a server it can use.</summary>
public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute() => Skip = SqlServerProbe.SkipReason;
}
