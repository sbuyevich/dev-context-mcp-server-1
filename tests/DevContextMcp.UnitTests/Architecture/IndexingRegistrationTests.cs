using DevContextMcp.Indexer.Core;
using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Services;
using DevContextMcp.Infrastructure;
using DevContextMcp.Server.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.UnitTests.Architecture;

public sealed class IndexingRegistrationTests
{
    [Fact]
    public void IndexerRegistersOrchestrationWithoutConcreteAdapters()
    {
        var services = new ServiceCollection();

        services.AddIndexer();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IIndexCoordinator));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IIndexStore));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IPackageSourceClient));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IPackageProcessor));
    }

    [Fact]
    public void InfrastructureRegistersIndexerPortImplementations()
    {
        var services = new ServiceCollection();

        services.AddIndexingInfrastructure();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IIndexStore>());
        Assert.NotNull(provider.GetService<IPackageSourceClient>());
        Assert.NotNull(provider.GetService<IPackageProcessor>());
        Assert.NotNull(provider.GetService<IContentHasher>());
        Assert.NotNull(provider.GetService<IDocumentChunker>());
        Assert.NotNull(provider.GetService<INuGetSourceAuthenticationProvider>());
    }

    [Fact]
    public void InfrastructureRegistersServerDiagnosticPortImplementation()
    {
        var services = new ServiceCollection();

        services.AddRetrievalInfrastructure();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ILocalDependencyCheck>());
    }
}
