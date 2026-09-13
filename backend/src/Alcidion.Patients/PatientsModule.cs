using Alcidion.Patients.Application;
using Alcidion.Patients.Domain;
using Alcidion.Patients.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Patients;

public static class PatientsModule
{
    /// <summary>
    /// Registers the Patients context against the in-memory store. Used by tests and by a run with
    /// no database configured, so the API still starts and the domain still holds its invariants.
    /// </summary>
    public static IServiceCollection AddPatientsDomain(this IServiceCollection services) =>
        services.AddPatientsDomain(null);

    /// <summary>
    /// Registers the Patients context, backed by SQL Server when a connection string is supplied.
    /// The store is the only thing that changes: the aggregate, the service and the repository
    /// contract are the same either way, which is the point of keeping persistence behind
    /// <see cref="IPatientRepository"/>.
    /// </summary>
    public static IServiceCollection AddPatientsDomain(this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IPatientRepository, InMemoryPatientRepository>();
        }
        else
        {
            services.AddDbContext<PatientsDbContext>(o => o.UseSqlServer(connectionString));
            services.AddScoped<IPatientRepository, EfPatientRepository>();
        }

        services.AddScoped<PatientService>();
        return services;
    }
}
