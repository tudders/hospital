using Alcidion.Patients.Application;
using Alcidion.Patients.Contracts;
using Alcidion.Patients.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcidion.Domain.Tests.Patients;

public class PatientServiceTests
{
    private readonly RecordingEventBus _bus = new();
    private readonly FakeClock _clock = new(TestServices.Now);
    private readonly PatientService _sut;

    public PatientServiceTests()
    {
        _sut = new PatientService(new InMemoryPatientRepository(), _bus, _clock, NullLogger<PatientService>.Instance);
    }

    private static RegisterPatientCommand Valid(string mrn = "MRN-1") => new(mrn, "Ada", "Lovelace", new DateOnly(1990, 1, 1));

    [Fact]
    public async Task Register_persists_and_publishes_PatientRegistered()
    {
        var result = await _sut.RegisterAsync(Valid());

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(_bus.Published.OfType<PatientRegistered>());
        Assert.Equal(result.Value!.Id, evt.PatientId);
        Assert.Equal("MRN-1", evt.Mrn);
        Assert.Equal("Ada Lovelace", evt.FullName);
        Assert.Equal(_clock.UtcNow, evt.OccurredAt);

        var fetched = await _sut.GetAsync(result.Value.Id);
        Assert.True(fetched.IsSuccess);
    }

    [Fact]
    public async Task Register_with_duplicate_mrn_is_a_conflict_and_publishes_nothing()
    {
        await _sut.RegisterAsync(Valid());
        _bus.Clear();

        var result = await _sut.RegisterAsync(Valid(mrn: "mrn-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error!.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Register_with_whitespace_padded_duplicate_mrn_is_a_conflict()
    {
        await _sut.RegisterAsync(Valid(mrn: "REVIEW-1"));

        var result = await _sut.RegisterAsync(Valid(mrn: " review-1 "));

        Assert.Equal("conflict", result.Error!.Code);
        Assert.Single(await _sut.ListAsync());
    }

    [Fact]
    public async Task Concurrent_registrations_of_the_same_mrn_yield_exactly_one_patient()
    {
        var results = Race.Run(16, _ => _sut.RegisterAsync(Valid(mrn: "RACE-1")).GetAwaiter().GetResult());

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("conflict", r.Error!.Code));
        Assert.Single(await _sut.ListAsync());
        Assert.Single(_bus.Published.OfType<PatientRegistered>());
    }

    [Fact]
    public async Task Register_with_invalid_data_is_a_validation_error()
    {
        var result = await _sut.RegisterAsync(Valid() with { GivenName = "" });

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Error!.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Get_unknown_patient_is_not_found()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        Assert.Equal("not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Correct_writes_the_named_fields_publishes_and_advances_the_version()
    {
        var patient = (await _sut.RegisterAsync(Valid())).Value!;
        _bus.Clear();

        var result = await _sut.CorrectAsync(patient.Id, new CorrectPatientCommand(null, "Augusta", null, null), patient.Version);

        Assert.True(result.IsSuccess);
        Assert.Equal("Augusta", result.Value!.GivenName);
        Assert.Equal("Lovelace", result.Value.FamilyName);
        Assert.Equal(patient.Version + 1, result.Value.Version);

        var evt = Assert.Single(_bus.Published.OfType<PatientCorrected>());
        Assert.Equal(patient.Id, evt.PatientId);
        Assert.Equal("Augusta Lovelace", evt.FullName);
        Assert.Equal(_clock.UtcNow, evt.OccurredAt);

        Assert.Equal("Augusta", (await _sut.GetAsync(patient.Id)).Value!.GivenName);
    }

    [Fact]
    public async Task A_replayed_correction_is_refused_rather_than_reinstating_what_it_changed()
    {
        var patient = (await _sut.RegisterAsync(Valid())).Value!;
        var change = new CorrectPatientCommand(null, "Augusta", null, null);
        await _sut.CorrectAsync(patient.Id, change, patient.Version);
        await _sut.CorrectAsync(patient.Id, new CorrectPatientCommand(null, "Ada", null, null), patient.Version + 1);
        _bus.Clear();

        // The replay still quotes the version it was first decided against, which the record has
        // moved twice past. Accepting it would undo the second correction with no sign it happened.
        var replay = await _sut.CorrectAsync(patient.Id, change, patient.Version);

        Assert.Equal("precondition_failed", replay.Error!.Code);
        Assert.Contains("version 2", replay.Error.Message);
        Assert.Equal("Ada", (await _sut.GetAsync(patient.Id)).Value!.GivenName);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Correcting_an_mrn_onto_one_another_patient_holds_is_a_conflict()
    {
        await _sut.RegisterAsync(Valid(mrn: "TAKEN-1"));
        var patient = (await _sut.RegisterAsync(Valid(mrn: "MINE-1"))).Value!;
        _bus.Clear();

        var result = await _sut.CorrectAsync(patient.Id, new CorrectPatientCommand(" taken-1 ", null, null, null), patient.Version);

        Assert.Equal("conflict", result.Error!.Code);
        Assert.Equal("MINE-1", (await _sut.GetAsync(patient.Id)).Value!.Mrn);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_corrected_mrn_releases_the_old_one_for_reuse()
    {
        var patient = (await _sut.RegisterAsync(Valid(mrn: "TYPO-1"))).Value!;

        await _sut.CorrectAsync(patient.Id, new CorrectPatientCommand("RIGHT-1", null, null, null), patient.Version);

        // The typo was never a real MRN, and the next patient through the door may well be the one
        // it was meant for. An index that held on to it would refuse them for no reason.
        Assert.True((await _sut.RegisterAsync(Valid(mrn: "TYPO-1"))).IsSuccess);
        Assert.Equal(patient.Id, Assert.Single(await _sut.SearchAsync("RIGHT-1")).Id);
    }

    [Fact]
    public async Task Correct_with_invalid_data_is_a_validation_error()
    {
        var patient = (await _sut.RegisterAsync(Valid())).Value!;
        _bus.Clear();

        var result = await _sut.CorrectAsync(patient.Id, new CorrectPatientCommand(null, " ", null, null), patient.Version);

        Assert.Equal("validation", result.Error!.Code);
        Assert.Equal("Ada", (await _sut.GetAsync(patient.Id)).Value!.GivenName);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Correct_unknown_patient_is_not_found()
    {
        var result = await _sut.CorrectAsync(Guid.NewGuid(), new CorrectPatientCommand(null, "Ada", null, null), 0);

        Assert.Equal("not_found", result.Error!.Code);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Concurrent_corrections_of_one_patient_leave_exactly_one_winner()
    {
        var patient = (await _sut.RegisterAsync(Valid())).Value!;

        var results = Race.Run(16, i => _sut
            .CorrectAsync(patient.Id, new CorrectPatientCommand(null, $"Name{i}", null, null), patient.Version)
            .GetAwaiter().GetResult());

        // All sixteen were decided against version 0, so exactly one can be taken against it. The
        // other fifteen are behind, which is a precondition failure and not a conflict: nothing
        // about them is wrong.
        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("precondition_failed", r.Error!.Code));
        Assert.Equal(1, (await _sut.GetAsync(patient.Id)).Value!.Version);
        Assert.Single(_bus.Published.OfType<PatientCorrected>());
    }
}
