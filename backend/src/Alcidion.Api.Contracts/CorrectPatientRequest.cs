using System.Globalization;
using Alcidion.Patients.Application;
using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>PATCH /api/patients/{id}</c>, generated from
/// <c>Schemas/correct-patient-request.json</c>.
/// </summary>
/// <remarks>
/// The generated type is a view over pooled JSON and belongs at the controller edge only.
/// <see cref="ToCommand"/> is the one place it turns into something the domain owns - and the one
/// place the difference between "absent" and "present" is read, because that difference is the whole
/// of PATCH semantics. An absent property reads back as <c>Undefined</c>, so the kind is what is
/// checked rather than the value.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/correct-patient-request.json")]
public readonly partial struct CorrectPatientRequest
{
    /// <summary>
    /// Hands the correction to the domain as a plain record, with null for every field the caller
    /// left out. Every conversion here is safe because the schema has already been evaluated - the
    /// formatter fails the request before the action runs.
    /// </summary>
    public CorrectPatientCommand ToCommand() => new(
        Mrn.ValueKind is JsonValueKind.String ? (string)Mrn : null,
        GivenName.ValueKind is JsonValueKind.String ? (string)GivenName : null,
        FamilyName.ValueKind is JsonValueKind.String ? (string)FamilyName : null,
        DateOfBirth.ValueKind is JsonValueKind.String
            ? DateOnly.ParseExact((string)DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null);
}
