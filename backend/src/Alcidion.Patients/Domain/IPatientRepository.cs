namespace Alcidion.Patients.Domain;

/// <summary>Repository: persistence abstraction owned by the domain.</summary>
public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Patient?> GetByMrnAsync(string mrn, CancellationToken ct = default);
    Task<IReadOnlyList<Patient>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Patient>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Inserts the patient only if its MRN is unused, atomically. Returns false when the MRN is
    /// already taken. This is the single enforcement point for MRN uniqueness: a check followed by
    /// a separate insert would let two concurrent callers both pass the check. A relational
    /// implementation backs this with a unique index and maps the violation to false.
    /// </summary>
    Task<bool> TryAddAsync(Patient patient, CancellationToken ct = default);
}
