using Alcidion.Patients.Domain;
using Alcidion.Patients.Contracts;
using Alcidion.Shared;
using Alcidion.Shared.Events;
using Microsoft.Extensions.Logging;

namespace Alcidion.Patients.Application;

/// <summary>Application service (use-case orchestrator). Depends only on abstractions (DIP).</summary>
public sealed class PatientService(
    IPatientRepository repository,
    IEventBus eventBus,
    IClock clock,
    ILogger<PatientService> logger)
{
    public async Task<Result<Patient>> RegisterAsync(RegisterPatientCommand cmd, CancellationToken ct = default)
    {
        Patient patient;
        try
        {
            patient = Patient.Register(cmd.Mrn, cmd.GivenName, cmd.FamilyName, cmd.DateOfBirth, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Result<Patient>.Fail(Error.Validation(ex.Message));
        }

        // Uniqueness is decided by the insert itself, on the normalized MRN the aggregate produced.
        // Checking first and inserting after would accept duplicates when two requests race.
        if (!await repository.TryAddAsync(patient, ct))
            return Result<Patient>.Fail(Error.Conflict($"A patient with MRN '{patient.Mrn}' already exists."));

        logger.LogInformation("Registered patient {PatientId} ({Mrn})", patient.Id, patient.Mrn);

        await eventBus.PublishAsync(new PatientRegistered(patient.Id, patient.Mrn, patient.FullName, clock.UtcNow), ct);
        return Result<Patient>.Ok(patient);
    }

    public async Task<Result<Patient>> GetAsync(Guid id, CancellationToken ct = default) =>
        await repository.GetByIdAsync(id, ct) is { } p
            ? Result<Patient>.Ok(p)
            : Result<Patient>.Fail(Error.NotFound("Patient", id));

    public Task<IReadOnlyList<Patient>> ListAsync(CancellationToken ct = default) => repository.ListAsync(ct);

    public Task<IReadOnlyList<Patient>> SearchAsync(string query, CancellationToken ct = default) =>
        repository.SearchAsync(query, ct);

    /// <summary>
    /// Corrects demographics on a registered patient. <paramref name="expectedVersion"/> is the
    /// version the caller decided against: it makes the correction conditional, so a stale view of
    /// the record is refused rather than writing over whatever moved it on.
    /// </summary>
    /// <remarks>
    /// The version is checked here as well as at the write, and the check here is the courtesy one:
    /// it answers a caller that is obviously behind without touching the row. The write's own check
    /// is the enforcement, because only it closes the window between this read and that update.
    /// </remarks>
    public async Task<Result<Patient>> CorrectAsync(Guid id, CorrectPatientCommand cmd, long expectedVersion, CancellationToken ct = default)
    {
        var patient = await repository.GetByIdAsync(id, ct);
        if (patient is null) return Result<Patient>.Fail(Error.NotFound("Patient", id));
        if (patient.Version != expectedVersion) return Result<Patient>.Fail(Stale(patient.Version));

        try
        {
            patient.Correct(cmd.Mrn, cmd.GivenName, cmd.FamilyName, cmd.DateOfBirth, clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Result<Patient>.Fail(Error.Validation(ex.Message));
        }

        switch (await repository.TryCorrectAsync(patient, expectedVersion, ct))
        {
            case CorrectionResult.Corrected(var corrected):
                logger.LogInformation("Corrected patient {PatientId} ({Mrn}) to version {Version}",
                    corrected.Id, corrected.Mrn, corrected.Version);
                await eventBus.PublishAsync(
                    new PatientCorrected(corrected.Id, corrected.Mrn, corrected.FullName, clock.UtcNow), ct);
                return Result<Patient>.Ok(corrected);

            case CorrectionResult.NotFound:
                return Result<Patient>.Fail(Error.NotFound("Patient", id));

            case CorrectionResult.VersionMismatch(var current):
                return Result<Patient>.Fail(Stale(current));

            case CorrectionResult.MrnTaken(var mrn):
                return Result<Patient>.Fail(Error.Conflict($"A patient with MRN '{mrn}' already exists."));

            case var unexpected:
                throw new InvalidOperationException($"Unhandled correction outcome {unexpected.GetType().Name}.");
        }
    }

    /// <summary>
    /// The patient moved on between being read and being written. Names the version it is at now, so
    /// a caller can re-read and retry without guessing whether it was worth retrying.
    /// </summary>
    private static Error Stale(long currentVersion) => Error.PreconditionFailed(
        $"Patient has changed since it was read; it is now at version {currentVersion}. Re-read it and try again.");
}
