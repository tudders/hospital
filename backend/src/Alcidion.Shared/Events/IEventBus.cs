namespace Alcidion.Shared.Events;

/// <summary>
/// GoF Mediator: publishers don't know subscribers. Domains talk only through this.
/// Swap the in-memory implementation for a broker (Azure Service Bus, RabbitMQ) without touching domains.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent;
}
