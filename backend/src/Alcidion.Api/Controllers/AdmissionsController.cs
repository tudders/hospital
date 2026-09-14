using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Api.Auth;
using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Alcidion.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <summary>An admission episode, its ward and the version required to change it.</summary>
/// <param name="Version">
/// What the admission's ETag is built from, and what a change to it has to be taken against. It is
/// on the list representation as well as the single one so the client that drives this API - a
/// board showing every admission - can act on a row without fetching it again first.
/// </param>
public sealed record AdmissionDto(Guid Id, Guid PatientId, string Ward, string Status, DateTimeOffset AdmittedAt, DateTimeOffset? DischargedAt, long Version)
{
    public static AdmissionDto From(Admission a) => new(a.Id, a.PatientId, a.Ward, a.Status.ToString(), a.AdmittedAt, a.DischargedAt, a.Version);
}

[Route("api/admissions")]
[Authorize]
public sealed class AdmissionsController(AdmissionService admissions) : ApiController
{
    [HttpGet]
    [EndpointName("ListAdmissions")]
    [EndpointSummary("List admissions")]
    [EndpointDescription("Returns admission episodes with their current ward, status and concurrency version.")]
    public async Task<ActionResult<IReadOnlyList<AdmissionDto>>> List(CancellationToken ct) =>
        Ok((await admissions.ListAsync(ct)).Select(AdmissionDto.From).ToList());

    /// <summary>
    /// One admission. This is what <c>Location</c> on a successful admit points at, and where the
    /// ETag a change has to quote comes from.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EndpointName("GetAdmission")]
    [EndpointSummary("Get an admission")]
    [EndpointDescription("Returns an admission and its ETag. Use that ETag in If-Match when changing the admission.")]
    [ProducesResponseType<AdmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdmissionDto>> Get(Guid id, CancellationToken ct)
    {
        if (await admissions.GetAsync(id, ct) is not { } admission)
            return FromError(Error.NotFound("Admission", id));

        return Ok(Tagged(admission));
    }

    [HttpPost]
    [EndpointName("AdmitPatient")]
    [EndpointSummary("Admit a patient")]
    [EndpointDescription("Starts an admission for a registered patient. Requires a clinician or administrator; returns Location and ETag on success.")]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.admit")]
    [ProducesResponseType<AdmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdmissionDto>> Admit([FromBody] AdmitPatientRequest body, CancellationToken ct)
    {
        var result = await admissions.AdmitAsync(body.ToCommand(), ct);
        if (result.IsSuccess) Telemetry.PatientsAdmitted.Add(1);
        return result.Match<ActionResult>(
            a => CreatedAtAction(nameof(Get), new { id = a.Id }, Tagged(a)),
            FromError);
    }

    /// <summary>
    /// The one way an admission changes after it is created: a destination ward transfers the
    /// patient, a status of <c>discharged</c> ends the episode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>If-Match</c> is required, not optional. Both transitions rewrite where the patient is, and
    /// both are reachable twice - a network retry, a proxy replay, two clinicians on the same board.
    /// Taken against a version, the second arrival is refused; taken against nothing, a replayed
    /// transfer closes the stay the first one opened, claims a second bed and answers 200 over a
    /// bed history that no longer describes the hospital.
    /// </para>
    /// <para>
    /// This is also why these are not <c>POST /{id}/transfer</c> and <c>POST /{id}/discharge</c> any
    /// more: POST promises nothing about a repeat, so the safety had to be bolted on beside the verb
    /// rather than expressed by it.
    /// </para>
    /// </remarks>
    [HttpPatch("{id:guid}")]
    [EndpointName("ChangeAdmission")]
    [EndpointSummary("Transfer or discharge a patient")]
    [EndpointDescription("Transfers to a destination ward or discharges an admission. Requires a clinician or administrator and a strong If-Match ETag; stale versions return 412 and missing preconditions return 428.")]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.admission_change")]
    [ProducesResponseType<AdmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<AdmissionDto>> Change(Guid id, [FromBody] ChangeAdmissionRequest body, CancellationToken ct)
    {
        if (ExpectedVersion() is not { } expected) return PreconditionRequired("admission");

        var ward = body.ToWard();
        HttpContext.RefineAudit(ward is null ? "patient.discharge" : "patient.transfer");

        var result = ward is null
            ? await admissions.DischargeAsync(id, expected, ct)
            : await admissions.TransferAsync(id, ward, expected, ct);

        return result.Match<ActionResult>(a => Ok(Tagged(a)), FromError);
    }

    /// <summary>The admission, with its version published as the response's ETag.</summary>
    private AdmissionDto Tagged(Admission admission)
    {
        PublishVersion(admission.Version);
        return AdmissionDto.From(admission);
    }
}
