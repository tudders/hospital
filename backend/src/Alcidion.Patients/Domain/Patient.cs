namespace Alcidion.Patients.Domain;

/// <summary>Aggregate root for the Patients bounded context.</summary>
public sealed class Patient
{
    public Guid Id { get; }

    /// <summary>Medical Record Number, unique per facility.</summary>
    public string Mrn { get; private set; }
    public string GivenName { get; private set; }
    public string FamilyName { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public DateTimeOffset RegisteredAt { get; }

    /// <summary>
    /// The stored version this instance was read at. Storage owns the number; the aggregate only
    /// carries it, so a correction can be taken under the version it was decided under and lose to
    /// anything that moved the patient on in between. Every accepted correction bumps it.
    /// </summary>
    public long Version { get; private set; }

    private readonly Lock _gate = new();

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
        RequireMrn(mrn);
        RequireName(givenName, nameof(givenName), "Given name");
        RequireName(familyName, nameof(familyName), "Family name");
        RequireBirthDate(dateOfBirth, now, nameof(dateOfBirth));

        return new Patient(Guid.NewGuid(), NormalizeMrn(mrn), givenName.Trim(), familyName.Trim(), dateOfBirth, now);
    }

    /// <summary>
    /// Rebuilds a patient from storage. Invariants are not re-run: a stored row was validated by
    /// <see cref="Register"/> on the way in, and re-validating would make a clock change or a rule
    /// change reject history that is already on file.
    /// </summary>
    public static Patient Rehydrate(Guid id, string mrn, string givenName, string familyName, DateOnly dateOfBirth,
        DateTimeOffset registeredAt, long version = 0) =>
        new(id, mrn, givenName, familyName, dateOfBirth, registeredAt) { Version = version };

    /// <summary>
    /// Records that this instance's state has been committed. Only a store calls it: the version is
    /// the store's, and an aggregate that bumped its own would report a version nothing was written at.
    /// </summary>
    internal void Committed() => Version++;

    /// <summary>
    /// Corrects demographics that were recorded wrong, or have since changed. A null argument means
    /// "leave this alone", so a correction says only what it is correcting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration is not re-run, because a correction is not a re-registration: <see cref="Id"/>
    /// and <see cref="RegisteredAt"/> are what tie every admission, bed stay and audit line to this
    /// patient, so neither is correctable. What is correctable is exactly what a human typed.
    /// </para>
    /// <para>
    /// Everything supplied is validated before anything is written. A correction that set the given
    /// name and then threw on the date of birth would leave the aggregate half-corrected, and the
    /// caller holding a patient that is neither what it was nor what was asked for.
    /// </para>
    /// </remarks>
    public void Correct(string? mrn, string? givenName, string? familyName, DateOnly? dateOfBirth, DateTimeOffset now)
    {
        if (mrn is null && givenName is null && familyName is null && dateOfBirth is null)
            throw new ArgumentException("A correction must change something.", nameof(mrn));

        if (mrn is not null) RequireMrn(mrn);
        if (givenName is not null) RequireName(givenName, nameof(givenName), "Given name");
        if (familyName is not null) RequireName(familyName, nameof(familyName), "Family name");
        if (dateOfBirth is { } dob) RequireBirthDate(dob, now, nameof(dateOfBirth));

        lock (_gate)
        {
            if (mrn is not null) Mrn = NormalizeMrn(mrn);
            if (givenName is not null) GivenName = givenName.Trim();
            if (familyName is not null) FamilyName = familyName.Trim();
            if (dateOfBirth is { } corrected) DateOfBirth = corrected;
        }
    }

    public string FullName => $"{GivenName} {FamilyName}";

    private static void RequireMrn(string mrn)
    {
        if (string.IsNullOrWhiteSpace(mrn)) throw new ArgumentException("MRN is required.", nameof(mrn));
    }

    private static void RequireName(string name, string parameter, string what)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException($"{what} is required.", parameter);
    }

    private static void RequireBirthDate(DateOnly dateOfBirth, DateTimeOffset now, string parameter)
    {
        if (dateOfBirth > DateOnly.FromDateTime(now.UtcDateTime))
            throw new ArgumentException("Date of birth cannot be in the future.", parameter);
    }
}
