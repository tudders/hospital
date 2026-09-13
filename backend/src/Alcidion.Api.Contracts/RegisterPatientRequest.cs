using System.Globalization;
using Alcidion.Patients.Application;
using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/patients</c>, generated from <c>Schemas/register-patient-request.json</c>.
/// </summary>
/// <remarks>
/// The generated type is a view over pooled JSON and belongs at the controller edge only.
/// <see cref="ToCommand"/> is the one place it turns into something the domain owns.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/register-patient-request.json")]
public readonly partial struct RegisterPatientRequest
{
    /// <summary>
    /// Hands the request to the domain as a plain record. Every conversion here is safe because the
    /// schema has already been evaluated - the formatter fails the request before the action runs.
    /// </summary>
    public RegisterPatientCommand ToCommand() => new(
        (string)Mrn,
        (string)GivenName,
        (string)FamilyName,
        DateOnly.ParseExact((string)DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture));
}
