using Alcidion.Admissions.Application;
using Alcidion.Admissions.Domain;
using Alcidion.Admissions.Infrastructure;
using Alcidion.Patients.Contracts;
using Alcidion.Shared.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Admissions;

public static class AdmissionsModule
{
    /// <summary>
    /// Registers the Admissions context against the in-memory store. Used by tests and by a run
    /// with no database configured, so the API still starts and the domain still holds its invariants.
    /// </summary>
    public static IServiceCollection AddAdmissionsDomain(this IServiceCollection services) =>
        services.AddAdmissionsDomain(null);

    /// <summary>
    /// Registers the Admissions context, backed by SQL Server when a connection string is supplied.
    /// Only the store changes: the aggregate, the service and the repository contract are the same
    /// either way, which is the point of keeping persistence behind <see cref="IAdmissionRepository"/>.
    /// </summary>
    public static IServiceCollection AddAdmissionsDomain(this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IAdmissionRepository, InMemoryAdmissionRepository>();
            services.AddSingleton<IKnownPatients, InMemoryKnownPatients>();
            services.AddSingleton<IWardDirectory, InMemoryWardDirectory>();
        }
        else
        {
            services.AddDbContext<AdmissionsDbContext>(o => o.UseSqlServer(connectionString));
            services.AddScoped<EfAdmissionRepository>();
            services.AddScoped<IAdmissionRepository>(sp => sp.GetRequiredService<EfAdmissionRepository>());
            services.AddScoped<IKnownPatients, EfKnownPatients>();
            services.AddScoped<IWardDirectory, EfWardDirectory>();
        }

        services.AddScoped<AdmissionService>();
        services.AddScoped<IEventHandler<PatientRegistered>, PatientRegisteredHandler>();
        services.AddScoped<IEventHandler<PatientCorrected>, PatientCorrectedHandler>();
        return services;
    }
}
