using Alcidion.Admissions.Domain;
using Alcidion.Patients.Contracts;
using Alcidion.Shared.Events;
using Microsoft.Extensions.Logging;

namespace Alcidion.Admissions.Application;

/// <summary>
/// Cross-context subscriber (GoF Observer). Keeps Admissions' patient read model current when the
/// Patients context corrects an MRN or a name.
/// </summary>
/// <remarks>
/// Without this, a corrected patient would go on being admitted and discharged under the MRN a clerk
/// already fixed - and the copy would drift further with every correction, because nothing else ever
/// revisits it. The SQL-backed read model projects the patient row directly and so has nothing to do
/// here, which is the same asymmetry <see cref="PatientRegisteredHandler"/> already lives with.
/// </remarks>
public sealed class PatientCorrectedHandler(IKnownPatients knownPatients, ILogger<PatientCorrectedHandler> logger)
    : IEventHandler<PatientCorrected>
{
    public async Task HandleAsync(PatientCorrected @event, CancellationToken ct = default)
    {
        await knownPatients.UpsertAsync(new KnownPatient(@event.PatientId, @event.Mrn, @event.FullName), ct);
        logger.LogInformation("Admissions took the correction to patient {PatientId} ({Mrn})", @event.PatientId, @event.Mrn);
    }
}
