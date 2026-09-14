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

        // The write decides whether the patient already has an active admission and whether a bed
        // was there to take, so two concurrent admits cannot both pass a separate check.
        var stored = await admissions.TryAddActiveAsync(admission, ct) switch
        {
            AdmitResult.Admitted(var persisted) => Result<Admission>.Ok(persisted),
            AdmitResult.AlreadyActive(var active) =>
                Result<Admission>.Fail(Error.Conflict($"Patient is already admitted to ward '{active.Ward}'.")),
            AdmitResult.UnknownWard(var ward) =>
                Result<Admission>.Fail(Error.Validation($"No ward matches '{ward}'.")),
            AdmitResult.NoBedAvailable(var ward, var reason) =>
                Result<Admission>.Fail(Error.Conflict($"No bed available in ward '{ward}': {reason}")),
            var unexpected => throw new InvalidOperationException($"Unhandled admit outcome {unexpected.GetType().Name}."),
        };
        if (!stored.IsSuccess) return stored;

        admission = stored.Value!;
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

        // The aggregate's own lock only guards one instance; a second request holds its own copy of
        // the same admission, so the store has the final say on who discharged it.
        if (!await admissions.UpdateAsync(admission, ct))
            return Result<Admission>.Fail(Error.Conflict("Admission is already discharged."));

        logger.LogInformation("Discharged admission {AdmissionId} for patient {PatientId}", admission.Id, admission.PatientId);
        await eventBus.PublishAsync(new PatientDischarged(admission.Id, admission.PatientId, clock.UtcNow), ct);
        return Result<Admission>.Ok(admission);
    }

    public Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) => admissions.ListAsync(ct);

    public async Task<Result<Admission>> TransferAsync(Guid admissionId, string ward, CancellationToken ct = default)
    {
        var result = await admissions.TransferAsync(admissionId, ward, clock.UtcNow, ct);
        return result switch
        {
            TransferResult.Transferred(var admission) => Result<Admission>.Ok(admission),
            TransferResult.NotFound => Result<Admission>.Fail(Error.NotFound("Admission", admissionId)),
            TransferResult.UnknownWard(var requestedWard) => Result<Admission>.Fail(Error.Validation($"No ward matches '{requestedWard}'.")),
            TransferResult.NoBedAvailable(var destination, var reason) => Result<Admission>.Fail(Error.Conflict($"No bed available in ward '{destination}': {reason}")),
            var unexpected => throw new InvalidOperationException($"Unhandled transfer outcome {unexpected.GetType().Name}."),
        };
    }
}
