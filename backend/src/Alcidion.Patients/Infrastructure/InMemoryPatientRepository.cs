using System.Collections.Concurrent;
using Alcidion.Patients.Domain;

namespace Alcidion.Patients.Infrastructure;

/// <summary>
/// In-memory stand-in for a real store. The MRN index is what a unique index would be in a
/// database: reserving a key with TryAdd is the atomic step that makes uniqueness hold under
/// concurrency, rather than a read-then-write that two callers can interleave.
/// </summary>
public sealed class InMemoryPatientRepository : IPatientRepository
{
    private readonly ConcurrentDictionary<Guid, Patient> _store = new();
    private readonly ConcurrentDictionary<string, Guid> _byMrn = new(StringComparer.Ordinal);

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task<Patient?> GetByMrnAsync(string mrn, CancellationToken ct = default) =>
        Task.FromResult(_byMrn.TryGetValue(Patient.NormalizeMrn(mrn), out var id) ? _store.GetValueOrDefault(id) : null);

    public Task<IReadOnlyList<Patient>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Patient>>(_store.Values.OrderBy(p => p.RegisteredAt).ThenBy(p => p.Mrn, StringComparer.Ordinal).ToList());

    public Task<bool> TryAddAsync(Patient patient, CancellationToken ct = default)
    {
        if (!_byMrn.TryAdd(patient.Mrn, patient.Id)) return Task.FromResult(false);
        _store[patient.Id] = patient;
        return Task.FromResult(true);
    }
}
