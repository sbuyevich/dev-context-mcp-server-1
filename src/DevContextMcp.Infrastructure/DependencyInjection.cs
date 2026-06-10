using DevContextMcp.Server.Core.Retrieval.Abstractions;
using DevContextMcp.Infrastructure.Diagnostics;
using DevContextMcp.Infrastructure.Indexing.Abstractions;
using DevContextMcp.Infrastructure.Indexing.NuGet;
using DevContextMcp.Infrastructure.Indexing.Persistence;
using DevContextMcp.Infrastructure.Indexing.Processing;
using DevContextMcp.Infrastructure.Retrieval;
using DevContextMcp.Indexer.Core.Abstractions;
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
