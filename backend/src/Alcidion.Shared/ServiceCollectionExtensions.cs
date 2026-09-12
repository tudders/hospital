using Alcidion.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Alcidion.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        // Scoped so handlers resolve from the request scope (a singleton would hold the root provider).
        services.AddScoped<IEventBus, InMemoryEventBus>();
        return services;
    }
}
