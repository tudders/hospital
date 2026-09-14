namespace Alcidion.Admissions.Domain;

/// <summary>
/// The outcome of an attempted admit. The store decides this, not a caller-side check: whether the
/// patient is already admitted and whether a bed can be claimed are both questions only the write
/// itself can answer without a race.
/// </summary>
public abstract record AdmitResult
{
    /// <summary>Persisted. Carries the stored admission, whose ward is the one actually allocated.</summary>
    public sealed record Admitted(Admission Admission) : AdmitResult;

    /// <summary>The patient already holds an open admission.</summary>
    public sealed record AlreadyActive(Admission Existing) : AdmitResult;

    /// <summary>No ward matched the requested name, code or type.</summary>
    public sealed record UnknownWard(string Ward) : AdmitResult;

    /// <summary>The ward exists but had no bed to give: none free, or none left within its staffed limit.</summary>
    public sealed record NoBedAvailable(string Ward, string Reason) : AdmitResult;
}

public abstract record TransferResult
{
    public sealed record Transferred(Admission Admission) : TransferResult;
    public sealed record NotFound : TransferResult;
    public sealed record UnknownWard(string Ward) : TransferResult;
    public sealed record NoBedAvailable(string Ward, string Reason) : TransferResult;
}

public interface IAdmissionRepository
{
    Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default);
    Task<TransferResult> TransferAsync(Guid admissionId, string ward, DateTimeOffset now, CancellationToken ct = default) =>
        Task.FromResult<TransferResult>(new TransferResult.NotFound());

    /// <summary>
    /// Admits the patient only if they have no open admission and a bed can be claimed in the
    /// requested ward, atomically. One enforcement point, so two concurrent admits cannot both pass
    /// a separate "is anyone admitted?" check, and two allocations cannot claim the same bed.
    /// A relational implementation backs the first with a partial unique index on (PatientId) where
    /// DischargedAt is null, and the second with a locking read inside the allocating transaction.
    /// </summary>
    Task<AdmitResult> TryAddActiveAsync(Admission admission, CancellationToken ct = default);

    /// <summary>
    /// Persists state changes on an admission, releasing its bed and its active slot once
    /// discharged. Returns false when the write lost a race - the admission moved on between being
    /// read and being written - which the caller reports as a conflict rather than a silent no-op.
    /// </summary>
    Task<bool> UpdateAsync(Admission admission, CancellationToken ct = default);
}
