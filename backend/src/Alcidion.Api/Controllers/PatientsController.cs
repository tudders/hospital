using Alcidion.Api.Auth;
using Alcidion.Api.Contracts;
using Alcidion.Api.Observability;
using Alcidion.Patients.Application;
using Alcidion.Patients.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcidion.Api.Controllers;

/// <summary>A registered patient's identifiers, demographics and concurrency version.</summary>
/// <param name="Version">
/// What the patient's ETag is built from, and what a correction to it has to be taken against. It is
/// on the list representation as well as the single one so a client showing a table of patients can
/// correct a row without fetching it again first.
/// </param>
public sealed record PatientDto(Guid Id, string Mrn, string GivenName, string FamilyName, DateOnly DateOfBirth, DateTimeOffset RegisteredAt, long Version)
{
    public static PatientDto From(Patient p) => new(p.Id, p.Mrn, p.GivenName, p.FamilyName, p.DateOfBirth, p.RegisteredAt, p.Version);
}

[Route("api/patients")]
[Authorize]
public sealed class PatientsController(PatientService patients) : ApiController
{
    /// <remarks>
    /// <c>search</c> puts a patient name in the query string, which reaches access logs, proxy logs,
    /// browser history and <c>Referer</c>. That is a decision rather than an oversight, and what it
    /// rests on is written down in docs/adr/0004-patient-search-stays-a-get.md.
    /// </remarks>
    [HttpGet]
    [EndpointName("ListPatients")]
    [EndpointSummary("List or search patients")]
    [EndpointDescription("Returns registered patients, optionally filtered by name or MRN. Search values contain patient information and must be redacted from access logs.")]
    [ProducesResponseType<IReadOnlyList<PatientDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PatientDto>>> List([FromQuery] string? search, CancellationToken ct)
    {
        var results = string.IsNullOrWhiteSpace(search)
            ? await patients.ListAsync(ct)
            : await patients.SearchAsync(search, ct);
        return Ok(results.Select(PatientDto.From).ToList());
    }

    /// <summary>
    /// One patient. This is what <c>Location</c> on a successful registration points at, and where
    /// the ETag a correction has to quote comes from.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EndpointName("GetPatient")]
    [EndpointSummary("Get a patient")]
    [EndpointDescription("Returns patient demographics and an ETag. Use that ETag in If-Match when correcting the record.")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDto>> Get(Guid id, CancellationToken ct) =>
        (await patients.GetAsync(id, ct)).Match<ActionResult>(p => Ok(Tagged(p)), FromError);

    [HttpPost]
    [EndpointName("RegisterPatient")]
    [EndpointSummary("Register a patient")]
    [EndpointDescription("Registers a patient with a unique MRN. Requires a clinician or administrator; returns Location and ETag on success.")]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.register")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> Register([FromBody] RegisterPatientRequest body, CancellationToken ct)
    {
        var result = await patients.RegisterAsync(body.ToCommand(), ct);
        if (result.IsSuccess) Telemetry.PatientsRegistered.Add(1);
        return result.Match<ActionResult>(
            p => CreatedAtAction(nameof(Get), new { id = p.Id }, Tagged(p)),
            FromError);
    }

    /// <summary>
    /// Corrects demographics: an MRN typed wrong, a legal name change, a date of birth off by a
    /// digit. Only the properties the body carries change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PATCH rather than PUT because a correction is partial by nature. A clerk fixing an MRN has no
    /// business restating a date of birth they did not check, and a PUT that omitted it would either
    /// erase it or quietly mean PATCH anyway.
    /// </para>
    /// <para>
    /// <c>If-Match</c> is required, for the reason it is required on an admission: demographics are
    /// exactly what two people correct at once - a clerk fixing the MRN while a nurse fixes the
    /// spelling of a name - and the loser of that race must be told, not silently dropped.
    /// </para>
    /// <para>
    /// There is no DELETE. A patient with admissions, bed stays and an audit trail behind them cannot
    /// be removed without taking the record of their care with it; a record entered in error is a
    /// correction or a merge, neither of which is what DELETE means.
    /// </para>
    /// </remarks>
    [HttpPatch("{id:guid}")]
    [EndpointName("CorrectPatient")]
    [EndpointSummary("Correct patient demographics")]
    [EndpointDescription("Changes only the supplied demographic fields. Requires a clinician or administrator and a strong If-Match ETag; stale versions return 412 and missing preconditions return 428.")]
    [Authorize(Policy = Policies.Clinician)]
    [Audited("patient.correct")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<PatientDto>> Correct(Guid id, [FromBody] CorrectPatientRequest body, CancellationToken ct)
    {
        if (ExpectedVersion() is not { } expected) return PreconditionRequired("patient");

        var result = await patients.CorrectAsync(id, body.ToCommand(), expected, ct);
        return result.Match<ActionResult>(p => Ok(Tagged(p)), FromError);
    }

    /// <summary>The patient, with its version published as the response's ETag.</summary>
    private PatientDto Tagged(Patient patient)
    {
        PublishVersion(patient.Version);
        return PatientDto.From(patient);
    }
}
