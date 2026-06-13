using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Demo.Cities;

/// <summary>
/// Extension methods for registering Demo.Cities services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the general and United States city services with singleton lifetime.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddDemoCities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICityService, CityService>();
        services.TryAddSingleton<IUsaCityService, UsaCityService>();
        return services;
    }
}
