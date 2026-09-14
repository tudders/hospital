using System.Collections.Concurrent;
using Alcidion.Admissions.Domain;

namespace Alcidion.Admissions.Infrastructure;

/// <summary>
/// In-memory stand-in for a real store. The active-admission index plays the role of a partial
/// unique index: claiming the patient's slot with TryAdd is the atomic step that keeps "one active
/// admission per patient" true under concurrency.
/// </summary>
public sealed class InMemoryAdmissionRepository : IAdmissionRepository
{
    private readonly ConcurrentDictionary<Guid, Admission> _store = new();
    private readonly ConcurrentDictionary<Guid, Admission> _activeByPatient = new();

    public Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default) =>
        Task.FromResult(_activeByPatient.GetValueOrDefault(patientId));

    public Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Admission>>(_store.Values.OrderBy(a => a.AdmittedAt).ThenBy(a => a.Id).ToList());

    public Task<TransferResult> TransferAsync(Guid admissionId, string ward, DateTimeOffset now, long? expectedVersion = null, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(admissionId, out var admission) || admission.Status == AdmissionStatus.Discharged)
            return Task.FromResult<TransferResult>(new TransferResult.NotFound());
        // The version stands in for the SQL implementation's conditional update: a caller holding a
        // version this admission has already moved past is a replay, and replaying a transfer here
        // would be as wrong as it is against the real store.
        if (expectedVersion is { } expected && admission.Version != expected)
            return Task.FromResult<TransferResult>(new TransferResult.VersionMismatch(admission.Version));
        try { admission.Transfer(ward); }
        catch (InvalidOperationException) { return Task.FromResult<TransferResult>(new TransferResult.NotFound()); }
        admission.Committed();
        return Task.FromResult<TransferResult>(new TransferResult.Transferred(admission));
    }

    /// <summary>
    /// There are no beds to allocate here, so the only outcome this store can refuse is a patient
    /// who is already admitted. A ward is whatever text the caller asked for.
    /// </summary>
    public Task<AdmitResult> TryAddActiveAsync(Admission admission, CancellationToken ct = default)
    {
        var holder = _activeByPatient.GetOrAdd(admission.PatientId, admission);
        if (!ReferenceEquals(holder, admission)) return Task.FromResult<AdmitResult>(new AdmitResult.AlreadyActive(holder));

        _store[admission.Id] = admission;
        return Task.FromResult<AdmitResult>(new AdmitResult.Admitted(admission));
    }

    public Task<bool> UpdateAsync(Admission admission, CancellationToken ct = default)
    {
        // Same check as the SQL store's, on the only state there is: a write taken under a version
        // the stored admission has moved past lost, whatever this instance was told to do.
        if (_store.TryGetValue(admission.Id, out var stored) && stored.Version != admission.Version)
            return Task.FromResult(false);

        _store[admission.Id] = admission;
        if (admission.Status == AdmissionStatus.Discharged)
            _activeByPatient.TryRemove(new KeyValuePair<Guid, Admission>(admission.PatientId, admission));
        // The aggregate's lock already rejected the losing discharge before this was reached.
        admission.Committed();
        return Task.FromResult(true);
    }
}
