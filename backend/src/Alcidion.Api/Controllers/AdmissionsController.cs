using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Api.Auth;
using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

public sealed record AdmissionDto(Guid Id, Guid PatientId, string Ward, string Status, DateTimeOffset AdmittedAt, DateTimeOffset? DischargedAt)
{
    public static AdmissionDto From(Admission a) => new(a.Id, a.PatientId, a.Ward, a.Status.ToString(), a.AdmittedAt, a.DischargedAt);
}

[Route("api/admissions")]
[Authorize]
public sealed class AdmissionsController(AdmissionService admissions) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdmissionDto>>> List(CancellationToken ct) =>
        Ok((await admissions.ListAsync(ct)).Select(AdmissionDto.From).ToList());

    [HttpPost]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.admit")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdmissionDto>> Admit([FromBody] AdmitPatientRequest body, CancellationToken ct)
    {
        var result = await admissions.AdmitAsync(body.ToCommand(), ct);
        if (result.IsSuccess) Telemetry.PatientsAdmitted.Add(1);
        return result.Match<ActionResult>(
            a => Created($"/api/admissions/{a.Id}", AdmissionDto.From(a)),
            FromError);
    }

    [HttpPost("{id:guid}/discharge")]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.discharge")]
    public async Task<ActionResult<AdmissionDto>> Discharge(Guid id, CancellationToken ct) =>
        (await admissions.DischargeAsync(id, ct)).Match<ActionResult>(a => Ok(AdmissionDto.From(a)), FromError);
}
