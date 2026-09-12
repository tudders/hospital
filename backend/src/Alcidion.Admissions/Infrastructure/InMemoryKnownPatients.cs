using System.Collections.Concurrent;
using Alcidion.Admissions.Domain;

namespace Alcidion.Admissions.Infrastructure;

public sealed class InMemoryKnownPatients : IKnownPatients
{
    private readonly ConcurrentDictionary<Guid, KnownPatient> _store = new();

    public Task<KnownPatient?> FindAsync(Guid patientId, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(patientId));

    public Task UpsertAsync(KnownPatient patient, CancellationToken ct = default)
    {
        _store[patient.PatientId] = patient;
        return Task.CompletedTask;
    }
}
