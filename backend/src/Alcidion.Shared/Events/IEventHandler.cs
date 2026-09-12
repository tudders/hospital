namespace Alcidion.Shared.Events;

/// <summary>GoF Observer: a subscriber to a specific event type.</summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
