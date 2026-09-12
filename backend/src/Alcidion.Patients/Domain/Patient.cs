namespace Alcidion.Patients.Domain;

/// <summary>Aggregate root for the Patients bounded context.</summary>
public sealed class Patient
{
    public Guid Id { get; }
    public string Mrn { get; }          // Medical Record Number, unique per facility
    public string GivenName { get; }
    public string FamilyName { get; }
    public DateOnly DateOfBirth { get; }
    public DateTimeOffset RegisteredAt { get; }

    private Patient(Guid id, string mrn, string givenName, string familyName, DateOnly dateOfBirth, DateTimeOffset registeredAt)
    {
        Id = id; Mrn = mrn; GivenName = givenName; FamilyName = familyName; DateOfBirth = dateOfBirth; RegisteredAt = registeredAt;
    }

    /// <summary>
    /// Canonical form of an MRN. Uniqueness checks and storage must both go through this,
    /// otherwise " mrn-1 " and "MRN-1" are treated as different patients.
    /// </summary>
    public static string NormalizeMrn(string mrn) => (mrn ?? "").Trim().ToUpperInvariant();

    /// <summary>Static factory method: the only way to construct a valid Patient.</summary>
    public static Patient Register(string mrn, string givenName, string familyName, DateOnly dateOfBirth, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(mrn)) throw new ArgumentException("MRN is required.", nameof(mrn));
        if (string.IsNullOrWhiteSpace(givenName)) throw new ArgumentException("Given name is required.", nameof(givenName));
        if (string.IsNullOrWhiteSpace(familyName)) throw new ArgumentException("Family name is required.", nameof(familyName));
        if (dateOfBirth > DateOnly.FromDateTime(now.UtcDateTime)) throw new ArgumentException("Date of birth cannot be in the future.", nameof(dateOfBirth));

        return new Patient(Guid.NewGuid(), NormalizeMrn(mrn), givenName.Trim(), familyName.Trim(), dateOfBirth, now);
    }

    public string FullName => $"{GivenName} {FamilyName}";
}
