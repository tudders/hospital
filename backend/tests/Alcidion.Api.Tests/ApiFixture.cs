using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alcidion.Api.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // no console trace exporter noise
        builder.ConfigureLogging(l => l.AddProvider(Logs));
    }

    public async Task<HttpClient> ClientAs(string user)
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password = user });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private sealed record LoginBody(string AccessToken);
}

public sealed record CapturedLog(string Category, string Message, IReadOnlyList<string> Scopes);

/// <summary>
/// Records every log line with the scopes active when it was written, so tests can assert that a
/// correlation id reaches ordinary domain logs and not just the ones that print it by hand.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly List<CapturedLog> _lines = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    public IReadOnlyList<CapturedLog> Snapshot()
    {
        lock (_lines) return _lines.ToList();
    }

    private void Add(CapturedLog line)
    {
        lock (_lines) _lines.Add(line);
    }

    private sealed class Sink(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            owner._scopes.ForEachScope((scope, acc) => acc.Add(scope?.ToString() ?? ""), scopes);
            owner.Add(new CapturedLog(category, formatter(state, ex), scopes));
        }
    }
}
