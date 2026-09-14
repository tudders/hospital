using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Alcidion.Sql.Tests;

/// <summary>
/// A hospital database built from <c>backend/database/*.sql</c> for the length of a test run, then
/// dropped. The tracked migrations are the schema's source of truth (ADR 0002), so applying exactly
/// those files is what makes a passing test here mean something about the real database: the
/// triggers, check constraints, partial unique indexes and <c>datetimeoffset</c> precision the
/// repositories rely on are all present and all real.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private static readonly Regex BatchSeparator = new(@"^\s*GO(\s+\d+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private string? _database;

    /// <summary>The scratch database. Throws when the suite was skipped, which no test should reach.</summary>
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (SqlServerProbe.SkipReason is not null) return;

        // A name per run, so two checkouts (or a re-run over a leaked database) never share one.
        _database = $"alcidion_test_{Guid.NewGuid():N}";
        await RunAsync(SqlServerProbe.MasterConnectionString, $"CREATE DATABASE [{_database}];");

        ConnectionString = new SqlConnectionStringBuilder(SqlServerProbe.MasterConnectionString)
        {
            InitialCatalog = _database,
        }.ConnectionString;

        foreach (var file in MigrationFiles()) await ApplyAsync(file);
    }

    public async Task DisposeAsync()
    {
        if (_database is null) return;
        // A test that left a connection open would otherwise block the drop and leak the database.
        await RunAsync(SqlServerProbe.MasterConnectionString, $"""
            IF DB_ID('{_database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_database}];
            END
            """);
    }

    public SqlConnection Connect() => new(ConnectionString);

    /// <summary>Runs one statement against the scratch database. For arranging and asserting only.</summary>
    public Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters) =>
        RunAsync(ConnectionString, sql, parameters);

    /// <summary>Reads a single value out of the scratch database, or <c>default</c> when nothing matched.</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default;
        // DateTimeOffset is not IConvertible, so the common case has to be a plain cast.
        return value is T typed ? typed : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    /// <summary>Reads rows as object arrays, so an assertion can name the columns it cares about.</summary>
    public async Task<List<object?[]>> RowsAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            rows.Add([.. values.Select(v => v is DBNull ? null : v)]);
        }
        return rows;
    }

    /// <summary>
    /// Applies one migration the way sqlcmd would: every batch on the same connection, because the
    /// files open a transaction in the first batch and commit it in the last.
    /// </summary>
    private async Task ApplyAsync(string path)
    {
        await using var connection = Connect();
        await connection.OpenAsync();
        var batches = BatchSeparator.Split(await File.ReadAllTextAsync(path))
            .Where(b => b is not null && !string.IsNullOrWhiteSpace(b));

        foreach (var batch in batches)
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                await using var rollback = connection.CreateCommand();
                rollback.CommandText = "IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;";
                await rollback.ExecuteNonQueryAsync();
                throw new InvalidOperationException($"{Path.GetFileName(path)} failed to apply: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// The tracked migrations, in order. Found by walking up from the test binaries rather than by
    /// a copied path, so renaming an output directory cannot silently run the suite against nothing.
    /// </summary>
    private static IEnumerable<string> MigrationFiles()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var database = Path.Combine(dir.FullName, "database");
            if (!File.Exists(Path.Combine(database, "001_initial_hospital_schema.sql"))) continue;
            return Directory.EnumerateFiles(database, "*.sql").OrderBy(f => f, StringComparer.Ordinal);
        }

        throw new InvalidOperationException("Could not find backend/database above " + AppContext.BaseDirectory);
    }

    private static async Task RunAsync(string connectionString, string sql, params (string, object?)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand Command(SqlConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}

/// <summary>
/// One database per run, shared by every SQL-backed test. Building it takes seconds, so each test
/// seeds its own hospital inside it instead of paying for a new one.
/// </summary>
[CollectionDefinition(SqlServerCollection.Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
