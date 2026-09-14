using Alcidion.Patients.Domain;

namespace Alcidion.Domain.Tests.Patients;

public class PatientTests
{
    private static readonly DateTimeOffset Now = TestServices.Now;

    [Fact]
    public void Register_normalises_mrn_and_trims_names()
    {
        var p = Patient.Register(" mrn-001 ", "  Ada ", " Lovelace ", new DateOnly(1990, 1, 1), Now);

        Assert.Equal("MRN-001", p.Mrn);
        Assert.Equal("Ada", p.GivenName);
        Assert.Equal("Lovelace", p.FamilyName);
        Assert.Equal("Ada Lovelace", p.FullName);
        Assert.Equal(Now, p.RegisteredAt);
        Assert.NotEqual(Guid.Empty, p.Id);
    }

    [Theory]
    [InlineData("", "Ada", "Lovelace")]
    [InlineData("MRN-1", "", "Lovelace")]
    [InlineData("MRN-1", "Ada", " ")]
    public void Register_rejects_missing_required_fields(string mrn, string given, string family)
    {
        Assert.Throws<ArgumentException>(() => Patient.Register(mrn, given, family, new DateOnly(1990, 1, 1), Now));
    }

    [Fact]
    public void Register_rejects_future_date_of_birth()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        var ex = Assert.Throws<ArgumentException>(() => Patient.Register("MRN-1", "Ada", "Lovelace", tomorrow, Now));
        Assert.Equal("dateOfBirth", ex.ParamName);
    }

    [Fact]
    public void A_registered_patient_starts_at_version_zero()
    {
        // The column defaults to 0, so a freshly registered aggregate and a freshly inserted row
        // have to agree on what "never corrected" reads as - otherwise the first correction is
        // taken against a version nothing was ever written at.
        Assert.Equal(0, Patient.Register("MRN-1", "Ada", "Lovelace", new DateOnly(1990, 1, 1), Now).Version);
    }

    [Fact]
    public void Correct_changes_only_what_it_names()
    {
        var p = Registered();

        p.Correct(mrn: null, givenName: "Augusta", familyName: null, dateOfBirth: null, Now);

        Assert.Equal("Augusta", p.GivenName);
        Assert.Equal("MRN-001", p.Mrn);
        Assert.Equal("Lovelace", p.FamilyName);
        Assert.Equal(new DateOnly(1990, 1, 1), p.DateOfBirth);
    }

    [Fact]
    public void Correct_normalises_an_mrn_and_trims_a_name_the_way_registration_does()
    {
        var p = Registered();

        p.Correct(" mrn-002 ", "  Augusta ", " King-Noel ", new DateOnly(1815, 12, 10), Now);

        Assert.Equal("MRN-002", p.Mrn);
        Assert.Equal("Augusta", p.GivenName);
        Assert.Equal("King-Noel", p.FamilyName);
        Assert.Equal(new DateOnly(1815, 12, 10), p.DateOfBirth);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData(null, " ", null)]
    [InlineData(null, null, "")]
    public void Correct_rejects_blanking_a_required_field(string? mrn, string? given, string? family)
    {
        // An absent property means "leave this alone". A present but empty one means "make this
        // blank", which is the one thing a correction must not be able to do.
        Assert.Throws<ArgumentException>(() => Registered().Correct(mrn, given, family, null, Now));
    }

    [Fact]
    public void Correct_rejects_a_future_date_of_birth()
    {
        var tomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);
        var ex = Assert.Throws<ArgumentException>(() => Registered().Correct(null, null, null, tomorrow, Now));
        Assert.Equal("dateOfBirth", ex.ParamName);
    }

    [Fact]
    public void Correct_rejects_a_correction_that_corrects_nothing()
    {
        Assert.Throws<ArgumentException>(() => Registered().Correct(null, null, null, null, Now));
    }

    [Fact]
    public void A_refused_correction_leaves_the_patient_as_it_was()
    {
        var p = Registered();

        // The date of birth is the invalid one, and it is validated last. Applying the given name
        // on the way past would leave the caller holding a patient that is neither what it was nor
        // what was asked for.
        Assert.Throws<ArgumentException>(() =>
            p.Correct(null, "Augusta", null, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1), Now));

        Assert.Equal("Ada", p.GivenName);
    }

    [Fact]
    public void Correcting_does_not_move_the_version_by_itself()
    {
        // The version is the store's. An aggregate that bumped its own would report a version
        // nothing was written at, and the next correction would be taken against a number no row
        // ever held.
        var p = Registered();

        p.Correct(null, "Augusta", null, null, Now);

        Assert.Equal(0, p.Version);
    }

    private static Patient Registered() =>
        Patient.Register("MRN-001", "Ada", "Lovelace", new DateOnly(1990, 1, 1), Now);
}
