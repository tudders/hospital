namespace Alcidion.Patients.Domain;

/// <summary>
/// The outcome of an attempted correction. The store decides this, not a caller-side check: whether
/// the patient is still at the version the correction was decided against, and whether the corrected
/// MRN is free, are both questions only the write itself can answer without a race.
/// </summary>
public abstract record CorrectionResult
{
    /// <summary>Persisted. Carries the stored patient, at the version it now holds.</summary>
    public sealed record Corrected(Patient Patient) : CorrectionResult;

    /// <summary>No patient with that id.</summary>
    public sealed record NotFound : CorrectionResult;

    /// <summary>
    /// The patient exists, but not at the version the caller decided against: something corrected it
    /// in between. A replayed correction arrives here rather than re-applying itself over the newer one.
    /// </summary>
    public sealed record VersionMismatch(long CurrentVersion) : CorrectionResult;

    /// <summary>The corrected MRN already belongs to a different patient.</summary>
    public sealed record MrnTaken(string Mrn) : CorrectionResult;
}

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

    /// <summary>
    /// Writes a corrected patient, only if the stored row is still at
    /// <paramref name="expectedVersion"/>. The version is what makes the operation safe to repeat: a
    /// correction can arrive twice - a network retry, a proxy replay, two clerks on the same record -
    /// and the second must lose rather than reinstate a value the first one moved past.
    /// <para>
    /// The same enforcement point covers MRN uniqueness, for the same reason
    /// <see cref="TryAddAsync"/> does: correcting an MRN onto one another patient already holds is
    /// the same duplicate, arriving by a different verb.
    /// </para>
    /// </summary>
    Task<CorrectionResult> TryCorrectAsync(Patient corrected, long expectedVersion, CancellationToken ct = default);
}
