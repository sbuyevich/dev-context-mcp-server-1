using DevContextMcp.Indexer.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.Indexer.Core;

/// <summary>
/// Indexer use-case registration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIndexer(this IServiceCollection services)
    {
        services.AddSingleton<IIndexCoordinator, IndexCoordinator>();
        return services;
    }
}
