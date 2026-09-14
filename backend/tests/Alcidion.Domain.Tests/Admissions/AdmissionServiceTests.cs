using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Events;
using Alcidion.Admissions.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Domain.Tests.Admissions;

public class AdmissionServiceTests
{
    private readonly RecordingEventBus _bus = new();
    private readonly FakeClock _clock = new(TestServices.Now);
    private readonly InMemoryKnownPatients _known = new();
    private readonly AdmissionService _sut;
    private readonly Guid _patientId = Guid.NewGuid();

    public AdmissionServiceTests()
    {
        _sut = new AdmissionService(new InMemoryAdmissionRepository(), _known, _bus, _clock, NullLogger<AdmissionService>.Instance);
        _known.UpsertAsync(new KnownPatient(_patientId, "MRN-1", "Ada Lovelace")).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Admit_known_patient_succeeds_and_publishes_PatientAdmitted()
    {
        var result = await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward 3B"));

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(_bus.Published.OfType<PatientAdmitted>());
        Assert.Equal(result.Value!.Id, evt.AdmissionId);
        Assert.Equal("Ward 3B", evt.Ward);
    }

    [Fact]
    public async Task Admit_unknown_patient_names_the_field_that_is_wrong()
    {
        var result = await _sut.AdmitAsync(new AdmitPatientCommand(Guid.NewGuid(), "Ward"));

        // Not "not_found": what is missing is the patient the command points at, not the admissions
        // collection it is addressed to, and the difference is a 422 rather than a 404 at the edge.
        Assert.Equal("unprocessable_reference", result.Error!.Code);
        Assert.Equal("patientId", result.Error.Field);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Admit_twice_without_discharge_is_a_conflict()
    {
        await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward A"));

        var second = await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward B"));

        Assert.Equal("conflict", second.Error!.Code);
        Assert.Contains("Ward A", second.Error.Message);
    }

    [Fact]
    public async Task Concurrent_admissions_of_the_same_patient_yield_exactly_one_active_admission()
    {
        var results = Race.Run(16, i => _sut.AdmitAsync(new AdmitPatientCommand(_patientId, $"Ward {i}")).GetAwaiter().GetResult());

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("conflict", r.Error!.Code));
        Assert.Single(await _sut.ListAsync(), a => a.Status == AdmissionStatus.Admitted);
        Assert.Single(_bus.Published.OfType<PatientAdmitted>());
    }

    [Fact]
    public async Task Concurrent_discharges_of_one_admission_publish_a_single_event()
    {
        var admitted = await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward A"));
        _clock.Advance(TimeSpan.FromHours(1));

        var results = Race.Run(16, _ => _sut.DischargeAsync(admitted.Value!.Id).GetAwaiter().GetResult());

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("conflict", r.Error!.Code));
        Assert.Single(_bus.Published.OfType<PatientDischarged>());
    }

    [Fact]
    public async Task Discharge_then_readmit_is_allowed()
    {
        var first = await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward A"));
        _clock.Advance(TimeSpan.FromDays(1));

        var discharged = await _sut.DischargeAsync(first.Value!.Id);
        Assert.True(discharged.IsSuccess);
        Assert.Single(_bus.Published.OfType<PatientDischarged>());

        var again = await _sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward B"));
        Assert.True(again.IsSuccess);
    }

    [Fact]
    public async Task Discharge_unknown_admission_is_not_found()
    {
        var result = await _sut.DischargeAsync(Guid.NewGuid());
        Assert.Equal("not_found", result.Error!.Code);
    }
}
