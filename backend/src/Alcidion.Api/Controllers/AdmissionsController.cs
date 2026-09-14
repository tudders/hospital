using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Api.Auth;
using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Alcidion.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Alcidion.Api.Controllers;

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
    public async Task<ActionResult<IReadOnlyList<AdmissionDto>>> List(CancellationToken ct) =>
        Ok((await admissions.ListAsync(ct)).Select(AdmissionDto.From).ToList());

    /// <summary>
    /// One admission. This is what <c>Location</c> on a successful admit points at, and where the
    /// ETag a change has to quote comes from.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdmissionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdmissionDto>> Get(Guid id, CancellationToken ct)
    {
        if (await admissions.GetAsync(id, ct) is not { } admission)
            return FromError(Error.NotFound("Admission", id));

        return Ok(Tagged(admission));
    }

    [HttpPost]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.admit")]
    [ProducesResponseType<AdmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
        if (ExpectedVersion() is not { } expected)
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Precondition required",
                detail: "Send If-Match with the ETag of the admission you read. Without it a change "
                      + "someone else made in between would be overwritten rather than refused.");

        var ward = body.ToWard();
        HttpContext.RefineAudit(ward is null ? "patient.discharge" : "patient.transfer");

        var result = ward is null
            ? await admissions.DischargeAsync(id, expected, ct)
            : await admissions.TransferAsync(id, ward, expected, ct);

        return result.Match<ActionResult>(a => Ok(Tagged(a)), FromError);
    }

    /// <summary>
    /// The version this request is conditional on, or <see langword="null"/> when the caller sent no
    /// usable one. <c>If-Match: *</c> counts as none on purpose: it asserts only that the admission
    /// exists, which is not the question a transfer or a discharge has to be right about.
    /// </summary>
    private long? ExpectedVersion() =>
        EntityTagHeaderValue.TryParseList(Request.Headers.IfMatch, out var tags)
        && tags is [{ IsWeak: false, Tag.Length: > 2 } only]
        && long.TryParse(only.Tag.Value.AsSpan()[1..^1], out var version)
            ? version
            : null;

    /// <summary>
    /// Publishes the admission's version as its ETag, so the next change to it can be taken against
    /// the state the caller actually saw. Weak would be wrong here: the tag has to compare equal
    /// only to the exact version, which is what a strong comparison in <c>If-Match</c> means.
    /// </summary>
    private AdmissionDto Tagged(Admission admission)
    {
        Response.Headers.ETag = $"\"{admission.Version}\"";
        return AdmissionDto.From(admission);
    }
}
