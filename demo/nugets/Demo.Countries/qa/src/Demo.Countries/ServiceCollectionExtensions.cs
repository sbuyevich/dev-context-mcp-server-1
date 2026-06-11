using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Demo.Countries;

/// <summary>
/// Extension methods for registering Demo.Countries services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the country service with singleton lifetime.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddDemoCountries(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICountryService, CountryService>();
        return services;
    }
}
