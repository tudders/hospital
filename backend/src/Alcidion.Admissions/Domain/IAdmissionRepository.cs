namespace Alcidion.Admissions.Domain;

public interface IAdmissionRepository
{
    Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts the admission only if the patient has no active admission, atomically. Returns null
    /// on success, or the admission that already holds the patient. One enforcement point, so two
    /// concurrent admits cannot both pass a separate "is anyone admitted?" check.
    /// A relational implementation backs this with a partial unique index on (PatientId) where
    /// DischargedAt is null.
    /// </summary>
    Task<Admission?> TryAddActiveAsync(Admission admission, CancellationToken ct = default);

    /// <summary>Persists state changes on an admission, releasing its active slot once discharged.</summary>
    Task UpdateAsync(Admission admission, CancellationToken ct = default);
}
