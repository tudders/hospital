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
}
