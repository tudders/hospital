using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Infrastructure;
using Alcidion.Patients.Contracts;
using Alcidion.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Admissions;

public static class AdmissionsModule
{
    public static IServiceCollection AddAdmissionsDomain(this IServiceCollection services)
    {
        services.AddSingleton<IAdmissionRepository, InMemoryAdmissionRepository>();
        services.AddSingleton<IKnownPatients, InMemoryKnownPatients>();
        services.AddScoped<AdmissionService>();
        services.AddScoped<IEventHandler<PatientRegistered>, PatientRegisteredHandler>();
        return services;
    }
}
