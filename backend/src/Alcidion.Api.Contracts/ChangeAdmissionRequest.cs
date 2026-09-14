using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>PATCH /api/admissions/{id}</c>, generated from
/// <c>Schemas/change-admission-request.json</c>.
/// </summary>
/// <remarks>
/// One body describes one transition, which is the schema's job rather than the action's:
/// <c>minProperties</c>, <c>maxProperties</c> and <c>additionalProperties: false</c> together mean a
/// request naming both a ward and a status never reaches the controller, so there is no "which did
/// they mean" branch to get wrong.
/// </remarks>
[JsonSchemaTypeGenerator("Schemas/change-admission-request.json")]
public readonly partial struct ChangeAdmissionRequest
{
    /// <summary>
    /// The destination ward when the change is a transfer, otherwise <see langword="null"/>. An
    /// absent property reads back as <c>Undefined</c>, so the kind is what is checked rather than
    /// the value.
    /// </summary>
    public string? ToWard() => Ward.ValueKind is JsonValueKind.String ? (string)Ward : null;

    /// <summary>True when the change ends the episode. The schema admits no other status.</summary>
    public bool IsDischarge => Status.ValueKind is JsonValueKind.String;
}
