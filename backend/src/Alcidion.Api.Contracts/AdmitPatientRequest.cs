using Alcidion.Admissions.Application;
using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/admissions</c>, generated from <c>Schemas/admit-patient-request.json</c>.
/// </summary>
/// <remarks>
/// The generated type is a view over pooled JSON and belongs at the controller edge only.
/// <see cref="ToCommand"/> is the one place it turns into something the domain owns.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/admit-patient-request.json")]
public readonly partial struct AdmitPatientRequest
{
    /// <summary>
    /// Hands the request to the application service as a plain record. Both conversions are safe
    /// because the schema has already been evaluated - the formatter fails the request before the
    /// action runs, so a body that reaches here has a patientId the "uuid" format accepted.
    /// </summary>
    public AdmitPatientCommand ToCommand() => new(Guid.Parse((string)PatientId), (string)Ward);
}
