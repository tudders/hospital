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

    /// <summary>
    /// The admission is open, but not at the version the caller decided against: something moved it
    /// on in between. A replayed transfer arrives here rather than moving the patient a second time.
    /// </summary>
    public sealed record VersionMismatch(long CurrentVersion) : TransferResult;
}

public interface IAdmissionRepository
{
    Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default);
    /// <summary>
    /// Moves the patient to another ward, taking the admission at <paramref name="expectedVersion"/>
    /// when one is given. The version is what makes the operation safe to repeat: POST and PATCH
    /// alike can arrive twice - a network retry, a proxy replay - and without it the second call
    /// closes the stay the first one opened, claims a second bed and writes a duplicate request.
    /// </summary>
    Task<TransferResult> TransferAsync(Guid admissionId, string ward, DateTimeOffset now, long? expectedVersion = null, CancellationToken ct = default) =>
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
    /// <para>
    /// The race is decided on <see cref="Admission.Version"/>, not on the admission still being
    /// open: a transfer that commits in between leaves it open but moves the patient to a stay this
    /// discharge's timestamp predates, and closing the admission over that would strand the bed.
    /// </para>
    /// </summary>
    Task<bool> UpdateAsync(Admission admission, CancellationToken ct = default);
}
