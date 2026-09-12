using Alcidion.Admissions.Domain;

namespace Alcidion.Domain.Tests.Admissions;

public class AdmissionTests
{
    private static readonly DateTimeOffset Now = TestServices.Now;

    [Fact]
    public void Admit_creates_active_admission()
    {
        var a = Admission.Admit(Guid.NewGuid(), " Ward 3B ", Now);

        Assert.Equal("Ward 3B", a.Ward);
        Assert.Equal(AdmissionStatus.Admitted, a.Status);
        Assert.Null(a.DischargedAt);
    }

    [Fact]
    public void Admit_requires_patient_and_ward()
    {
        Assert.Throws<ArgumentException>(() => Admission.Admit(Guid.Empty, "Ward", Now));
        Assert.Throws<ArgumentException>(() => Admission.Admit(Guid.NewGuid(), "", Now));
    }

    [Fact]
    public void Discharge_sets_status_and_cannot_be_repeated()
    {
        var a = Admission.Admit(Guid.NewGuid(), "Ward", Now);

        a.Discharge(Now.AddHours(2));

        Assert.Equal(AdmissionStatus.Discharged, a.Status);
        Assert.Equal(Now.AddHours(2), a.DischargedAt);
        Assert.Throws<InvalidOperationException>(() => a.Discharge(Now.AddHours(3)));
    }

    [Fact]
    public void Discharge_cannot_precede_admission()
    {
        var a = Admission.Admit(Guid.NewGuid(), "Ward", Now);
        Assert.Throws<ArgumentException>(() => a.Discharge(Now.AddMinutes(-1)));
    }
}
