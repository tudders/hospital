namespace Alcidion.Admissions.Domain;

/// <summary>
/// Admissions' own read model of patients, fed by events from the Patients context.
/// Admissions never calls into Patients directly; the event contract is the only seam.
/// </summary>
public interface IKnownPatients
{
    Task<KnownPatient?> FindAsync(Guid patientId, CancellationToken ct = default);
    Task UpsertAsync(KnownPatient patient, CancellationToken ct = default);
}

public sealed record KnownPatient(Guid PatientId, string Mrn, string FullName);
