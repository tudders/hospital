using Alcidion.Hospital.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Hospital;

public static class HospitalModule
{
    /// <summary>
    /// Registers the hospital occupancy read model, backed by SQL Server when a connection string
    /// is supplied. Unlike the writing contexts there is no in-memory stand-in: occupancy is a read
    /// of the real hospital or it is nothing, so without a connection string the view reports
    /// itself unavailable and the API still starts.
    /// </summary>
    public static IServiceCollection AddHospitalReadModel(this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddScoped<IHospitalOccupancyReader, UnavailableHospitalOccupancyReader>();
        }
        else
        {
            services.AddDbContext<OccupancyDbContext>(o => o.UseSqlServer(connectionString));
            services.AddScoped<IHospitalOccupancyReader, EfHospitalOccupancyReader>();
        }

        return services;
    }
}
