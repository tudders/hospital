using Corvus.Text.Json;

namespace Alcidion.Api.Contracts;

/// <summary>
/// Body of <c>POST /api/admissions/{id}/transfer</c>, generated from
/// <c>Schemas/transfer-patient-request.json</c>.
/// </summary>
[JsonSchemaTypeGenerator("Schemas/transfer-patient-request.json")]
public readonly partial struct TransferPatientRequest
{
    /// <summary>Returns the validated destination ward name.</summary>
    public string ToWard() => (string)Ward;
}
