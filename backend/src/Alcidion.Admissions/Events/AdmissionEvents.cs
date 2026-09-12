using Alcidion.Shared.Events;

namespace Alcidion.Admissions.Events;

public sealed record PatientAdmitted(Guid AdmissionId, Guid PatientId, string Ward, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

public sealed record PatientDischarged(Guid AdmissionId, Guid PatientId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
