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

        var result = await coordinator.IndexAllAsync(CancellationToken.None);
        var summary = Assert.Single(result.Summaries);

        Assert.Equal("failed", summary.Status);
        Assert.Empty(Assert.IsType<IndexSourceDefinition>(store.PublishedSource)
            .DeletedPackageIds);
    }

    [Fact]
    public async Task DiscoveredCountsDistinctPackageIdsIgnoringCase()
    {
        var source = new IndexSourceDefinition(
            "test",
            "test",
            "fixture",
            [
                new PackageSelectionDefinition("Alpha.Package", false, false, 2),
                new PackageSelectionDefinition("Beta.Package", false, false, 1)
            ],
            [],
            10);
        var coordinator = new IndexCoordinator(
            new StubConfigurationProvider(source),
            new CandidatePackageSourceClient(
            [
                new("Alpha.Package", "1.0.0", true, false, null),
                new("alpha.package", "2.0.0", true, false, null),
                new("Beta.Package", "1.0.0", true, false, null)
            ]),
            new UnexpectedPackageProcessor(),
            new CapturingIndexStore());

        var result = await coordinator.IndexAllAsync(CancellationToken.None);
        var summary = Assert.Single(result.Summaries);

        Assert.Equal(2, summary.Discovered);
        Assert.Equal(0, summary.Indexed);
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

    private sealed class CandidatePackageSourceClient(
        IReadOnlyList<PackageVersionCandidate> candidates) : IPackageSourceClient
    {
        public Task<IReadOnlyList<PackageVersionCandidate>> DiscoverAsync(
            IndexSourceDefinition source,
            CancellationToken cancellationToken) =>
            Task.FromResult(candidates);

        public Task<DownloadedPackage> DownloadAsync(
            IndexSourceDefinition source,
            PackageVersionCandidate package,
            PackageProcessingLimits limits,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("download failed");
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

        public Task<IReadOnlyList<IndexedLibrary>> GetIndexedLibrariesAsync(
            string databasePath,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexedLibrary>>([]);

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
