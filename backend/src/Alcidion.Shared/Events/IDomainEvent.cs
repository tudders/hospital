namespace Alcidion.Shared.Events;

/// <summary>
/// Marker for something that happened in one bounded context that other contexts may react to.
/// Events are immutable facts, named in the past tense.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
