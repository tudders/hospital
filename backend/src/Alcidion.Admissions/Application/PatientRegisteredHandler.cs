using Alcidion.Admissions.Domain;
using Alcidion.Patients.Contracts;
using Alcidion.Shared.Events;
using Microsoft.Extensions.Logging;

namespace Alcidion.Admissions.Application;

/// <summary>
/// Cross-context subscriber (GoF Observer). The only coupling to Patients is the event contract assembly.
/// </summary>
public sealed class PatientRegisteredHandler(IKnownPatients knownPatients, ILogger<PatientRegisteredHandler> logger)
    : IEventHandler<PatientRegistered>
{
    public async Task HandleAsync(PatientRegistered @event, CancellationToken ct = default)
    {
        await knownPatients.UpsertAsync(new KnownPatient(@event.PatientId, @event.Mrn, @event.FullName), ct);
        logger.LogInformation("Admissions now knows patient {PatientId} ({Mrn})", @event.PatientId, @event.Mrn);
    }
}
