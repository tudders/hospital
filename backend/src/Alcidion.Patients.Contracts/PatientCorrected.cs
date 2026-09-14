using Alcidion.Shared.Events;

namespace Alcidion.Patients.Contracts;

/// <summary>
/// Published by the Patients context when demographics are corrected; consumed by Admissions to keep
/// its own patient read model current.
/// </summary>
/// <remarks>
/// Deliberately not a second <see cref="PatientRegistered"/>. A subscriber that stores the identity
/// of a new patient and a subscriber that revises one it already holds are not the same handler, and
/// an event whose name says "registered" would make every replay of the stream register the patient
/// again. The payload matches because what a reader needs is the same either way: who the patient is
/// now.
/// </remarks>
public sealed record PatientCorrected(
    Guid PatientId,
    string Mrn,
    string FullName,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}
