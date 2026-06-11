using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class IndexCoordinatorTests
{
    [Fact]
    public async Task DiscoveryFailureDoesNotPublishDeleteTombstones()
    {
        var source = new IndexSourceDefinition(
            "test",
            "test",
            "fixture",
            [new PackageSelectionDefinition("Active.Package", false, false, 1)],
            ["Deleted.Package"],
            10);
        var store = new CapturingIndexStore();
        var coordinator = new IndexCoordinator(
            new StubConfigurationProvider(source),
            new FailingPackageSourceClient(),
            new UnexpectedPackageProcessor(),
            store);

        var summary = Assert.Single(await coordinator.IndexAllAsync(
            CancellationToken.None));

        Assert.Equal("failed", summary.Status);
        Assert.Empty(Assert.IsType<IndexSourceDefinition>(store.PublishedSource)
            .DeletedPackageIds);
    }

    private sealed class StubConfigurationProvider(IndexSourceDefinition source) :
        IIndexingConfigurationProvider
    {
        public IndexingSettings GetSettings() =>
            new(
                "fixture.db",
                new PackageProcessingLimits(
                    1,
                    1,
                    1,
                    1,
                    1,
                    1,
                    TimeSpan.FromSeconds(1)),
                [source]);
    }

    private sealed class FailingPackageSourceClient : IPackageSourceClient
    {
        public Task<IReadOnlyList<PackageVersionCandidate>> DiscoverAsync(
            IndexSourceDefinition source,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("discovery failed");

        public Task<DownloadedPackage> DownloadAsync(
            IndexSourceDefinition source,
            PackageVersionCandidate package,
            PackageProcessingLimits limits,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Download should not be called.");
    }

    private sealed class UnexpectedPackageProcessor : IPackageProcessor
    {
        public Task<PackageIndexData> ProcessAsync(
            PackageVersionCandidate candidate,
            DownloadedPackage package,
            PackageProcessingLimits limits,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Processor should not be called.");
    }

    private sealed class CapturingIndexStore : IIndexStore
    {
        public IndexSourceDefinition? PublishedSource { get; private set; }

        public Task InitializeAsync(
            string databasePath,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IndexPublishResult> PublishSourceAsync(
            string databasePath,
            IndexSourceDefinition source,
            DateTimeOffset startedAt,
            IReadOnlyList<PackageIndexData> packages,
            IReadOnlyCollection<PackageIdentityKey> retainedPackages,
            IReadOnlyList<IndexRunError> errors,
            bool pruneMissing,
            CancellationToken cancellationToken)
        {
            PublishedSource = source;
            return Task.FromResult(new IndexPublishResult(0, 0, [], [], []));
        }
    }
}
