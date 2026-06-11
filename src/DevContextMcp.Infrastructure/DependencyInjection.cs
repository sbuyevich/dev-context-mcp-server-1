using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Infrastructure.Diagnostics;
using DevContextMcp.Infrastructure.Indexer.NuGet;
using DevContextMcp.Infrastructure.Indexer.Persistence;
using DevContextMcp.Infrastructure.Indexer.Processing;
using DevContextMcp.Infrastructure.Server;
using DevContextMcp.Server.Core.Diagnostics;
using DevContextMcp.Server.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevContextMcp.Infrastructure;

/// <summary>
/// Infrastructure service registration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddRetrievalInfrastructure(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ILocalDependencyCheck, LocalDependencyCheck>();
        services.AddSingleton<INuGetReadStore, SqliteNuGetReadStore>();
        return services;
    }

    public static IServiceCollection AddIndexingInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();
        services.AddSingleton<IDocumentChunker, DocumentChunker>();
        services.AddSingleton<
            INuGetSourceAuthenticationProvider,
            AnonymousNuGetSourceAuthenticationProvider>();
        services.AddSingleton<IPackageSourceClient, NuGetPackageSourceClient>();
        services.AddSingleton<IPackageProcessor, NuGetPackageProcessor>();
        services.AddSingleton<IIndexStore, SqliteIndexStore>();
        return services;
    }
}
