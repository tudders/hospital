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

    public Task<Admission?> GetAsync(Guid admissionId, CancellationToken ct = default) =>
        admissions.GetByIdAsync(admissionId, ct);

    /// <summary>
    /// Ends the episode. <paramref name="expectedVersion"/> is the version the caller decided
    /// against: supplying it makes the discharge conditional, so a stale view of the admission is
    /// refused instead of writing over whatever moved it on.
    /// </summary>
    public async Task<Result<Admission>> DischargeAsync(Guid admissionId, long? expectedVersion = null, CancellationToken ct = default)
    {
        var admission = await admissions.GetByIdAsync(admissionId, ct);
        if (admission is null) return Result<Admission>.Fail(Error.NotFound("Admission", admissionId));
        if (expectedVersion is { } expected && admission.Version != expected)
            return Result<Admission>.Fail(Stale(admission.Version));

        try
        {
            admission.Discharge(clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Admission>.Fail(Error.Conflict(ex.Message));
        }

        // The aggregate's own lock only guards one instance; a second request holds its own copy of
        // the same admission, so the store has the final say on who discharged it. It says so by
        // rejecting the version this discharge was read at, which a transfer bumps as readily as a
        // second discharge - so the reason is read back rather than assumed.
        if (!await admissions.UpdateAsync(admission, ct))
        {
            var current = await admissions.GetByIdAsync(admissionId, ct);
            return Result<Admission>.Fail(current is null or { Status: AdmissionStatus.Discharged }
                ? Error.Conflict("Admission is already discharged.")
                : Stale(current.Version));
        }

        logger.LogInformation("Discharged admission {AdmissionId} for patient {PatientId}", admission.Id, admission.PatientId);
        await eventBus.PublishAsync(new PatientDischarged(admission.Id, admission.PatientId, clock.UtcNow), ct);
        return Result<Admission>.Ok(admission);
    }

    public Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) => admissions.ListAsync(ct);

    /// <summary>
    /// Moves the patient to another ward. <paramref name="expectedVersion"/> is what makes the call
    /// safe to repeat: without it a replayed transfer closes the stay the first one opened, claims a
    /// second bed and writes a duplicate request, and answers 200 over the result.
    /// </summary>
    public async Task<Result<Admission>> TransferAsync(Guid admissionId, string ward, long? expectedVersion = null, CancellationToken ct = default)
    {
        var result = await admissions.TransferAsync(admissionId, ward, clock.UtcNow, expectedVersion, ct);
        return result switch
        {
            TransferResult.Transferred(var admission) => Result<Admission>.Ok(admission),
            TransferResult.NotFound => Result<Admission>.Fail(Error.NotFound("Admission", admissionId)),
            TransferResult.VersionMismatch(var current) => Result<Admission>.Fail(Stale(current)),
            TransferResult.UnknownWard(var requestedWard) => Result<Admission>.Fail(Error.Validation($"No ward matches '{requestedWard}'.")),
            TransferResult.NoBedAvailable(var destination, var reason) => Result<Admission>.Fail(Error.Conflict($"No bed available in ward '{destination}': {reason}")),
            var unexpected => throw new InvalidOperationException($"Unhandled transfer outcome {unexpected.GetType().Name}."),
        };
    }

    /// <summary>
    /// The admission moved on between being read and being written. Names the version it is at now,
    /// so a caller can re-read and retry without guessing whether it was worth retrying.
    /// </summary>
    private static Error Stale(long currentVersion) => Error.PreconditionFailed(
        $"Admission has changed since it was read; it is now at version {currentVersion}. Re-read it and try again.");
}
