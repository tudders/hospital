using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Events;
using Alcidion.Shared;
using Alcidion.Shared.Events;
using Microsoft.Extensions.Logging;

namespace Alcidion.Admissions.Application;

public sealed record AdmitPatientCommand(Guid PatientId, string Ward);

/// <summary>Application service (use-case orchestrator). Depends only on abstractions (DIP).</summary>
public sealed class AdmissionService(
    IAdmissionRepository admissions,
    IKnownPatients knownPatients,
    IEventBus eventBus,
    IClock clock,
    ILogger<AdmissionService> logger)
{
    public async Task<Result<Admission>> AdmitAsync(AdmitPatientCommand cmd, CancellationToken ct = default)
    {
        if (await knownPatients.FindAsync(cmd.PatientId, ct) is null)
            return Result<Admission>.Fail(Error.NotFound("Patient", cmd.PatientId));

        Admission admission;
        try
        {
            admission = Admission.Admit(cmd.PatientId, cmd.Ward, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Result<Admission>.Fail(Error.Validation(ex.Message));
        }

        // The insert decides whether the patient already has an active admission, so two
        // concurrent admits cannot both pass a separate check.
        if (await admissions.TryAddActiveAsync(admission, ct) is { } active)
            return Result<Admission>.Fail(Error.Conflict($"Patient is already admitted to ward '{active.Ward}'."));

        logger.LogInformation("Admitted patient {PatientId} to {Ward} as admission {AdmissionId}",
            admission.PatientId, admission.Ward, admission.Id);
        await eventBus.PublishAsync(new PatientAdmitted(admission.Id, admission.PatientId, admission.Ward, clock.UtcNow), ct);
        return Result<Admission>.Ok(admission);
    }

    public async Task<Result<Admission>> DischargeAsync(Guid admissionId, CancellationToken ct = default)
    {
        var admission = await admissions.GetByIdAsync(admissionId, ct);
        if (admission is null) return Result<Admission>.Fail(Error.NotFound("Admission", admissionId));

        try
        {
            admission.Discharge(clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Admission>.Fail(Error.Conflict(ex.Message));
        }

        await admissions.UpdateAsync(admission, ct);
        logger.LogInformation("Discharged admission {AdmissionId} for patient {PatientId}", admission.Id, admission.PatientId);
        await eventBus.PublishAsync(new PatientDischarged(admission.Id, admission.PatientId, clock.UtcNow), ct);
        return Result<Admission>.Ok(admission);
    }

    public Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) => admissions.ListAsync(ct);
}
