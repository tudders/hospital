using Alcidion.Patients.Application;
using Alcidion.Patients.Domain;
using Alcidion.Patients.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Patients;

public static class PatientsModule
{
    public static IServiceCollection AddPatientsDomain(this IServiceCollection services)
    {
        services.AddSingleton<IPatientRepository, InMemoryPatientRepository>();
        services.AddScoped<PatientService>();
        return services;
    }
}
