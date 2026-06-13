using Microsoft.Extensions.DependencyInjection;

namespace OpenMeteo.Api.Client;

/// <summary>
/// Extension methods for registering the Open-Meteo API client.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOpenMeteoClient"/> as a typed HTTP client.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>A builder that can configure handlers and other HTTP client behavior.</returns>
    public static IHttpClientBuilder AddOpenMeteoApiClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>();
    }

    /// <summary>
    /// Registers <see cref="IOpenMeteoClient"/> as a configured typed HTTP client.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configureClient">A delegate used to configure the managed HTTP client.</param>
    /// <returns>A builder that can configure handlers and other HTTP client behavior.</returns>
    public static IHttpClientBuilder AddOpenMeteoApiClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureClient);

        return services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>(configureClient);
    }
}
