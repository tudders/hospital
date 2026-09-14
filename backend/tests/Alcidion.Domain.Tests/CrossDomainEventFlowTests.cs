using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Patients.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Domain.Tests;

/// <summary>
/// Proves the seam: Admissions learns about patients only via the PatientRegistered event,
/// using the real InMemoryEventBus and both modules' DI registrations.
/// </summary>
public class CrossDomainEventFlowTests
{
    [Fact]
    public async Task Registering_a_patient_makes_it_admittable()
    {
        using var provider = TestServices.BuildRealWiring();
        using var scope = provider.CreateScope();
        var patients = scope.ServiceProvider.GetRequiredService<PatientService>();
        var admissions = scope.ServiceProvider.GetRequiredService<AdmissionService>();
        var known = scope.ServiceProvider.GetRequiredService<IKnownPatients>();

        var registered = await patients.RegisterAsync(new RegisterPatientCommand("MRN-9", "Grace", "Hopper", new DateOnly(1906, 12, 9)));
        Assert.True(registered.IsSuccess);

        var projection = await known.FindAsync(registered.Value!.Id);
        Assert.NotNull(projection);
        Assert.Equal("Grace Hopper", projection.FullName);

        var admitted = await admissions.AdmitAsync(new AdmitPatientCommand(registered.Value.Id, "ICU"));
        Assert.True(admitted.IsSuccess);
    }

    [Fact]
    public async Task Unregistered_patient_cannot_be_admitted()
    {
        using var provider = TestServices.BuildRealWiring();
        using var scope = provider.CreateScope();
        var admissions = scope.ServiceProvider.GetRequiredService<AdmissionService>();

        var result = await admissions.AdmitAsync(new AdmitPatientCommand(Guid.NewGuid(), "ICU"));

        Assert.Equal("unprocessable_reference", result.Error!.Code);
        Assert.Equal("patientId", result.Error.Field);
    }

    [Fact]
    public async Task Correcting_a_patient_reaches_the_admissions_read_model()
    {
        using var provider = TestServices.BuildRealWiring();
        using var scope = provider.CreateScope();
        var patients = scope.ServiceProvider.GetRequiredService<PatientService>();
        var known = scope.ServiceProvider.GetRequiredService<IKnownPatients>();

        var registered = await patients.RegisterAsync(new RegisterPatientCommand("MRN-TYPO", "Grace", "Hoper", new DateOnly(1906, 12, 9)));
        var patient = registered.Value!;

        var corrected = await patients.CorrectAsync(patient.Id,
            new CorrectPatientCommand("MRN-9", null, "Hopper", null), patient.Version);
        Assert.True(corrected.IsSuccess);

        // Without the event the copy would keep the misspelling for good: nothing else ever
        // revisits it, so every later admission and discharge would be recorded against the name a
        // clerk already fixed.
        var projection = await known.FindAsync(patient.Id);
        Assert.Equal("Grace Hopper", projection!.FullName);
        Assert.Equal("MRN-9", projection.Mrn);
    }
}
