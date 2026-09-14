using Microsoft.Data.SqlClient;

namespace Alcidion.Api.Configuration;

/// <summary>
/// Where the hospital database connection string comes from. One resolver, so the EF-backed
/// domains and the occupancy reader cannot end up pointed at different databases.
/// </summary>
public static class HospitalConnection
{
    /// <summary>Returns the configured connection string, or null when the API is to run without a database.</summary>
    public static string? Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var value = configuration.GetConnectionString("Hospital") ?? configuration["SQL_SERVE_CONNECTION_STRING"];
        if (value is null && environment.IsDevelopment())
        {
            // Development convenience only. Never expose this non-VITE value to browser code.
            var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../../../frontend/.env"));
            if (File.Exists(path))
            {
                const string key = "SQL_SERVE_CONNECTION_STRING=";
                value = File.ReadLines(path).Select(line => line.Trim())
                    .FirstOrDefault(line => line.StartsWith(key, StringComparison.Ordinal))?[key.Length..];
            }
        }

        value = value?.Trim().Trim('"', '\'');
        const string prefix = "ConnectionString=";
        var resolved = value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true ? value[prefix.Length..] : value;
        if (string.IsNullOrWhiteSpace(resolved)) return null;

        return environment.IsDevelopment() ? TrustLocalCertificate(resolved) : resolved;
    }

    /// <summary>
    /// A development SQL Server normally presents a self-signed certificate, which the client
    /// rejects because Encrypt now defaults to true - the connection reaches the server and then
    /// fails during login. Trusting it is a development-only default and only when the connection
    /// string has said nothing about TLS itself: anything it states explicitly is left alone, and
    /// outside development the string is used exactly as configured.
    /// </summary>
    private static string TrustLocalCertificate(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            // ShouldSerialize, not ContainsKey: the builder knows every keyword, so ContainsKey is
            // true even for one the connection string never mentioned.
            if (builder.ShouldSerialize("Encrypt") || builder.ShouldSerialize("TrustServerCertificate")) return connectionString;
            builder.TrustServerCertificate = true;
            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Malformed: hand it back untouched and let the caller report the connection failure.
            return connectionString;
        }
    }
}
