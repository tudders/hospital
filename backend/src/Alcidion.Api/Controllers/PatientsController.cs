using Alcidion.Api.Auth;
using Alcidion.Api.Observability;
using Alcidion.Patients.Application;
using Alcidion.Patients.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

public sealed record PatientDto(Guid Id, string Mrn, string GivenName, string FamilyName, DateOnly DateOfBirth, DateTimeOffset RegisteredAt)
{
    public static PatientDto From(Patient p) => new(p.Id, p.Mrn, p.GivenName, p.FamilyName, p.DateOfBirth, p.RegisteredAt);
}

[Route("api/patients")]
[Authorize]
public sealed class PatientsController(PatientService patients) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientDto>>> List(CancellationToken ct) =>
        Ok((await patients.ListAsync(ct)).Select(PatientDto.From).ToList());

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDto>> Get(Guid id, CancellationToken ct) =>
        (await patients.GetAsync(id, ct)).Match<ActionResult>(p => Ok(PatientDto.From(p)), FromError);

    [HttpPost]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> Register([FromBody] RegisterPatientCommand cmd, CancellationToken ct)
    {
        var result = await patients.RegisterAsync(cmd, ct);
        if (result.IsSuccess) Telemetry.PatientsRegistered.Add(1);
        return result.Match<ActionResult>(
            p => CreatedAtAction(nameof(Get), new { id = p.Id }, PatientDto.From(p)),
            FromError);
    }
}
