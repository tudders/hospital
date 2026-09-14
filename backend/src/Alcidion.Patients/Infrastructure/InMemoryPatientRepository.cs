using System.Collections.Concurrent;
using Alcidion.Patients.Domain;

namespace Alcidion.Patients.Infrastructure;

/// <summary>
/// In-memory stand-in for a real store. The MRN index is what a unique index would be in a
/// database: reserving a key with TryAdd is the atomic step that makes uniqueness hold under
/// concurrency, rather than a read-then-write that two callers can interleave.
/// </summary>
/// <remarks>
/// Reads hand out copies, the way a database does. Sharing the stored instance would mean a
/// correction that loses on its version had already mutated the store on its way to being refused -
/// a failure mode the SQL store cannot have, and therefore one the hermetic tests must not be
/// written against.
/// </remarks>
public sealed class InMemoryPatientRepository : IPatientRepository
{
    private readonly ConcurrentDictionary<Guid, Patient> _store = new();
    private readonly ConcurrentDictionary<string, Guid> _byMrn = new(StringComparer.Ordinal);

    /// <summary>Serialises the read-check-write a correction needs; the row lock a database takes.</summary>
    private readonly Lock _corrections = new();

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Detached(_store.GetValueOrDefault(id)));

    public Task<Patient?> GetByMrnAsync(string mrn, CancellationToken ct = default) =>
        Task.FromResult(Detached(_byMrn.TryGetValue(Patient.NormalizeMrn(mrn), out var id) ? _store.GetValueOrDefault(id) : null));

    public Task<IReadOnlyList<Patient>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Patient>>(_store.Values
            .OrderBy(p => p.RegisteredAt).ThenBy(p => p.Mrn, StringComparer.Ordinal)
            .Select(Detached).ToList()!);

    public Task<IReadOnlyList<Patient>> SearchAsync(string query, CancellationToken ct = default)
    {
        var wanted = query.Trim();
        return Task.FromResult<IReadOnlyList<Patient>>(_store.Values
            .Where(p => p.Mrn.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || p.GivenName.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || p.FamilyName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.GivenName, StringComparer.OrdinalIgnoreCase)
            .Select(Detached).ToList()!);
    }

    public Task<bool> TryAddAsync(Patient patient, CancellationToken ct = default)
    {
        if (!_byMrn.TryAdd(patient.Mrn, patient.Id)) return Task.FromResult(false);
        _store[patient.Id] = Detached(patient)!;
        return Task.FromResult(true);
    }

    public Task<CorrectionResult> TryCorrectAsync(Patient corrected, long expectedVersion, CancellationToken ct = default)
    {
        lock (_corrections)
        {
            if (!_store.TryGetValue(corrected.Id, out var stored))
                return Task.FromResult<CorrectionResult>(new CorrectionResult.NotFound());

            // The same check the SQL store makes in its WHERE clause: a correction taken under a
            // version the stored patient has moved past lost, whatever it was told to write.
            if (stored.Version != expectedVersion)
                return Task.FromResult<CorrectionResult>(new CorrectionResult.VersionMismatch(stored.Version));

            // Read out of the index rather than off the caller's patient: the MRN it arrived with is
            // the corrected one, and the entry to release is whichever key still points at this id.
            if (!stored.Mrn.Equals(corrected.Mrn, StringComparison.Ordinal))
            {
                if (!_byMrn.TryAdd(corrected.Mrn, corrected.Id))
                    return Task.FromResult<CorrectionResult>(new CorrectionResult.MrnTaken(corrected.Mrn));
                _byMrn.TryRemove(stored.Mrn, out _);
            }

            corrected.Committed();
            _store[corrected.Id] = Detached(corrected)!;
            return Task.FromResult<CorrectionResult>(new CorrectionResult.Corrected(corrected));
        }
    }

    /// <summary>A patient that shares no state with the stored one, at the same version.</summary>
    private static Patient? Detached(Patient? p) => p is null
        ? null
        : Patient.Rehydrate(p.Id, p.Mrn, p.GivenName, p.FamilyName, p.DateOfBirth, p.RegisteredAt, p.Version);
}
