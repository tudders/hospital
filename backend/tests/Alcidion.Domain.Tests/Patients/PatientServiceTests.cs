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
}
