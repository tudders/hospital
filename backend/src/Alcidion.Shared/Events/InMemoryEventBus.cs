using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alcidion.Shared.Events;

/// <summary>
/// Resolves every registered <see cref="IEventHandler{TEvent}"/> from DI and invokes them in order.
/// Handlers run in-process and synchronously with the publisher; a broker-backed version would enqueue instead.
/// </summary>
public sealed class InMemoryEventBus(IServiceProvider services, ILogger<InMemoryEventBus> logger) : IEventBus
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent
    {
        var handlers = services.GetServices<IEventHandler<TEvent>>().ToList();
        logger.LogInformation("Publishing {EventType} {EventId} to {HandlerCount} handler(s)",
            typeof(TEvent).Name, @event.EventId, handlers.Count);

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(@event, ct);
        }
    }
}
