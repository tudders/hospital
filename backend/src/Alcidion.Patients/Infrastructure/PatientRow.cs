using Alcidion.Patients.Domain;

namespace Alcidion.Patients.Infrastructure;

/// <summary>
/// The <c>dbo.patients</c> row. Kept separate from <see cref="Patient"/> so the aggregate stays
/// free of persistence concerns: the table carries columns the domain has no opinion about
/// (<c>gender</c>), and the aggregate has no setters for EF to write through.
/// </summary>
internal sealed class PatientRow
{
    public Guid Id { get; set; }
    public string Mrn { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }

    /// <summary>NOT NULL in the schema and not yet modelled by the aggregate; written as the column default.</summary>
    public string Gender { get; set; } = "unknown";
    public DateTimeOffset RegisteredAt { get; set; }

    public static PatientRow From(Patient p) => new()
    {
        Id = p.Id,
        Mrn = p.Mrn,
        GivenName = p.GivenName,
        FamilyName = p.FamilyName,
        DateOfBirth = p.DateOfBirth,
        RegisteredAt = p.RegisteredAt,
    };

    public Patient ToDomain() => Patient.Rehydrate(Id, Mrn, GivenName, FamilyName, DateOfBirth, RegisteredAt);
}
