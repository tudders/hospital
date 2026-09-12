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
}
