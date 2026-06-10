using DevContextMcp.Indexer.Core;
using DevContextMcp.Indexer.Core.Abstractions;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Indexer;

/// <summary>
/// Indexer CLI composition and configuration registration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIndexerCli(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<INuGetPackageOptionsLoader, NuGetPackageOptionsLoader>();
        services.AddSingleton<IValidateOptions<IndexerOptions>>(provider =>
            new IndexerOptionsValidator(
                provider.GetRequiredService<INuGetPackageOptionsLoader>()));
        services.AddOptions<IndexerOptions>()
            .Bind(configuration.GetSection(IndexerOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IIndexingConfigurationProvider, OptionsIndexingConfigurationProvider>();
        services.AddSingleton<IndexerRunner>();
        services.AddIndexer();
        services.AddIndexingInfrastructure();

        return services;
    }
}
