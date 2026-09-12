using Alcidion.Shared.Events;

namespace Alcidion.Patients.Contracts;

/// <summary>Published by the Patients context; consumed by Admissions to build its own patient read model.</summary>
public sealed record PatientRegistered(
    Guid PatientId,
    string Mrn,
    string FullName,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
