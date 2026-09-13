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
        _store[admission.Id] = admission;
        if (admission.Status == AdmissionStatus.Discharged)
            _activeByPatient.TryRemove(new KeyValuePair<Guid, Admission>(admission.PatientId, admission));
        // The aggregate's lock already rejected the losing discharge before this was reached.
        return Task.FromResult(true);
    }
}
