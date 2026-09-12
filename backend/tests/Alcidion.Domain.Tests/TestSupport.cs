using Alcidion.Admissions;
using Alcidion.Patients;
using Alcidion.Shared;
using Alcidion.Shared.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Domain.Tests;

/// <summary>Deterministic clock for tests.</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Records every event published so tests can assert on the contract, not the side effects.</summary>
public sealed class RecordingEventBus : IEventBus
{
    private readonly List<IDomainEvent> _published = [];

    /// <summary>Snapshot, so concurrency tests can assert without racing the writer.</summary>
    public IReadOnlyList<IDomainEvent> Published
    {
        get { lock (_published) return _published.ToList(); }
    }

    public void Clear() { lock (_published) _published.Clear(); }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent
    {
        lock (_published) _published.Add(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Runs <paramref name="action"/> on dedicated threads released together by a barrier, so the
/// contended section really is entered concurrently. Task.Run is unsuitable here: blocking on a
/// barrier starves the thread pool and the release is drip-fed by thread injection.
/// </summary>
public static class Race
{
    public static IReadOnlyList<T> Run<T>(int threads, Func<int, T> action)
    {
        var results = new T[threads];
        var errors = new Exception?[threads];
        using var barrier = new Barrier(threads);
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                results[i] = action(i);
            }
            catch (Exception ex) { errors[i] = ex; }
        }) { IsBackground = true }).ToArray();

        foreach (var w in workers) w.Start();
        foreach (var w in workers) Assert.True(w.Join(TimeSpan.FromSeconds(10)), "A racing thread did not finish.");
        if (errors.OfType<Exception>().ToArray() is { Length: > 0 } thrown) throw new AggregateException(thrown);
        return results;
    }
}

public static class TestServices
{
    public static readonly DateTimeOffset Now = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Full in-process wiring of both domains with the real event bus, for cross-domain tests.</summary>
    public static ServiceProvider BuildRealWiring(FakeClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSharedKernel();
        services.AddSingleton<IClock>(clock ?? new FakeClock(Now));
        services.AddPatientsDomain();
        services.AddAdmissionsDomain();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
