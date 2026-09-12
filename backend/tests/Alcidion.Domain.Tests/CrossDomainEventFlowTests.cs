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

        Assert.Equal("not_found", result.Error!.Code);
    }
}
