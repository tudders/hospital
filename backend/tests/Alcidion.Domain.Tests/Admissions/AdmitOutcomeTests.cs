using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Events;
using Alcidion.Admissions.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Domain.Tests.Admissions;

/// <summary>
/// A persistent store can refuse an admission for reasons the in-memory one has no way to reach:
/// the ward may not exist, or it may have no bed to give. These assert that each refusal arrives at
/// the caller as the right kind of failure - a bad ward name is the request's fault (400), a full
/// ward is the hospital's state (409) - and that nothing is announced when the write did not happen.
/// </summary>
public class AdmitOutcomeTests
{
    private readonly RecordingEventBus _bus = new();
    private readonly InMemoryKnownPatients _known = new();
    private readonly Guid _patientId = Guid.NewGuid();

    public AdmitOutcomeTests() =>
        _known.UpsertAsync(new KnownPatient(_patientId, "MRN-1", "Ada Lovelace")).GetAwaiter().GetResult();

    private AdmissionService ServiceReturning(AdmitResult outcome) =>
        new(new StubRepository(outcome), _known, _bus, new FakeClock(TestServices.Now), NullLogger<AdmissionService>.Instance);

    [Fact]
    public async Task An_unmatched_ward_is_a_validation_failure()
    {
        var sut = ServiceReturning(new AdmitResult.UnknownWard("Ward 3B"));

        var result = await sut.AdmitAsync(new AdmitPatientCommand(_patientId, "Ward 3B"));

        Assert.Equal("validation", result.Error!.Code);
        Assert.Contains("Ward 3B", result.Error.Message);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_ward_with_no_bed_to_give_is_a_conflict_that_says_why()
    {
        var sut = ServiceReturning(new AdmitResult.NoBedAvailable("Intensive Care Unit", "30 of 30 staffed beds are in use"));

        var result = await sut.AdmitAsync(new AdmitPatientCommand(_patientId, "ICU"));

        Assert.Equal("conflict", result.Error!.Code);
        Assert.Contains("30 of 30 staffed beds", result.Error.Message);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task The_stored_admission_is_what_is_returned_and_announced()
    {
        // The store resolved "ICU" to the ward the allocated bed is actually in, so the caller and
        // the event must carry that, not the text the request happened to use.
        var stored = Admission.Admit(_patientId, "Intensive Care Unit", TestServices.Now);
        var sut = ServiceReturning(new AdmitResult.Admitted(stored));

        var result = await sut.AdmitAsync(new AdmitPatientCommand(_patientId, "ICU"));

        Assert.Equal("Intensive Care Unit", result.Value!.Ward);
        Assert.Equal("Intensive Care Unit", Assert.Single(_bus.Published.OfType<PatientAdmitted>()).Ward);
    }

    [Fact]
    public async Task A_discharge_the_store_refuses_is_a_conflict_and_publishes_nothing()
    {
        // The store rejected the update because someone else had already closed the admission.
        var sut = new AdmissionService(new StubRepository(null!, updates: false), _known, _bus,
            new FakeClock(TestServices.Now), NullLogger<AdmissionService>.Instance);
        var admission = Admission.Admit(_patientId, "ICU", TestServices.Now);

        var result = await sut.DischargeAsync(admission.Id);

        Assert.Equal("conflict", result.Error!.Code);
        Assert.Empty(_bus.Published.OfType<PatientDischarged>());
    }

    /// <summary>Answers with one fixed outcome, so the service's mapping is what is under test.</summary>
    private sealed class StubRepository(AdmitResult outcome, bool updates = true) : IAdmissionRepository
    {
        private readonly Admission _existing = Admission.Admit(Guid.NewGuid(), "Ward", TestServices.Now);

        public Task<AdmitResult> TryAddActiveAsync(Admission admission, CancellationToken ct = default) =>
            Task.FromResult(outcome);

        public Task<bool> UpdateAsync(Admission admission, CancellationToken ct = default) => Task.FromResult(updates);
        public Task<Admission?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Admission?>(_existing);
        public Task<Admission?> GetActiveForPatientAsync(Guid patientId, CancellationToken ct = default) => Task.FromResult<Admission?>(null);
        public Task<IReadOnlyList<Admission>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Admission>>([]);
    }
}
