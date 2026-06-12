using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
using Moq;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class IndexCoordinatorTests
{
    private const string DatabasePath = "fixture.db";

    private readonly Mock<IIndexingConfigurationProvider> _configurationProvider = new();
    private readonly Mock<IPackageSourceClient> _sourceClient = new();
    private readonly Mock<IPackageProcessor> _packageProcessor = new();
    private readonly Mock<IDocumentationSourceReader> _documentationReader = new();
    private readonly Mock<IIndexStore> _indexStore = new();
    private readonly IndexCoordinator _target;

    public IndexCoordinatorTests()
    {
        _target = new IndexCoordinator(
            _configurationProvider.Object,
            _sourceClient.Object,
            _packageProcessor.Object,
            _documentationReader.Object,
            _indexStore.Object);
    }

    // Purpose: publishes a failed source without applying configured delete tombstones
    [Fact]
    public async Task IndexAllAsync_DiscoveryFails_PublishesFailureWithoutDeleteTombstones()
    {
        // arrange
        var source = CreateSource(
            [new PackageSelectionDefinition("Active.Package", false, false, 1)],
            ["Deleted.Package"]);
        SetupCommon(CreateSettings(source));
        _sourceClient
            .Setup(client => client.DiscoverAsync(
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("discovery failed"));
        _indexStore
            .Setup(store => store.PublishSourceAsync(
                It.IsAny<string>(),
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<IReadOnlyList<PackageIndexData>>(),
                It.IsAny<IReadOnlyCollection<PackageIdentityKey>>(),
                It.IsAny<IReadOnlyList<IndexRunError>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPublishResult());

        // act
        var actual = await _target.IndexAllAsync(CancellationToken.None);

        // assert
        var summary = Assert.Single(actual.Summaries);
        Assert.Equal("failed", summary.Status);
        _configurationProvider.Verify(
            provider => provider.GetSettings(),
            Times.Once);
        _indexStore.Verify(
            store => store.InitializeAsync(
                DatabasePath,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _sourceClient.Verify(
            client => client.DiscoverAsync(
                source,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _indexStore.Verify(
            store => store.PublishSourceAsync(
                DatabasePath,
                It.Is<IndexSourceDefinition>(published =>
                    published.Name == source.Name
                    && published.DeletedPackageIds.Count == 0),
                It.IsAny<DateTimeOffset>(),
                It.Is<IReadOnlyList<PackageIndexData>>(packages =>
                    packages.Count == 0),
                It.Is<IReadOnlyCollection<PackageIdentityKey>>(retained =>
                    retained.Count == 0),
                It.Is<IReadOnlyList<IndexRunError>>(errors =>
                    errors.Count == 1
                    && errors[0].Code == "source_discovery_failed"),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _indexStore.Verify(
            store => store.GetIndexedLibrariesAsync(
                DatabasePath,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _sourceClient.Verify(
            client => client.DownloadAsync(
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<PackageVersionCandidate>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _packageProcessor.Verify(
            processor => processor.ProcessAsync(
                It.IsAny<PackageVersionCandidate>(),
                It.IsAny<DownloadedPackage>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyDocumentationNotCalled();
        VerifyNoOtherCalls();
    }

    // Purpose: counts discovered package identifiers case-insensitively when downloads fail
    [Fact]
    public async Task IndexAllAsync_DuplicatePackageIdCasing_CountsDistinctPackageIds()
    {
        // arrange
        IReadOnlyList<PackageVersionCandidate> candidates =
        [
            new("Alpha.Package", "1.0.0", true, false, null),
            new("alpha.package", "2.0.0", true, false, null),
            new("Beta.Package", "1.0.0", true, false, null)
        ];
        var source = CreateSource(
            [
                new PackageSelectionDefinition("Alpha.Package", false, false, 2),
                new PackageSelectionDefinition("Beta.Package", false, false, 1)
            ],
            []);
        SetupCommon(CreateSettings(source));
        _sourceClient
            .Setup(client => client.DiscoverAsync(
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);
        _sourceClient
            .Setup(client => client.DownloadAsync(
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<PackageVersionCandidate>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("download failed"));
        _indexStore
            .Setup(store => store.PublishSourceAsync(
                It.IsAny<string>(),
                It.IsAny<IndexSourceDefinition>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<IReadOnlyList<PackageIndexData>>(),
                It.IsAny<IReadOnlyCollection<PackageIdentityKey>>(),
                It.IsAny<IReadOnlyList<IndexRunError>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyPublishResult());

        // act
        var actual = await _target.IndexAllAsync(CancellationToken.None);

        // assert
        var summary = Assert.Single(actual.Summaries);
        Assert.Equal(2, summary.Discovered);
        Assert.Equal(0, summary.Indexed);
        _configurationProvider.Verify(
            provider => provider.GetSettings(),
            Times.Once);
        _indexStore.Verify(
            store => store.InitializeAsync(
                DatabasePath,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _sourceClient.Verify(
            client => client.DiscoverAsync(
                source,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _sourceClient.Verify(
            client => client.DownloadAsync(
                source,
                It.IsAny<PackageVersionCandidate>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(candidates.Count));
        _indexStore.Verify(
            store => store.PublishSourceAsync(
                DatabasePath,
                source,
                It.IsAny<DateTimeOffset>(),
                It.Is<IReadOnlyList<PackageIndexData>>(packages =>
                    packages.Count == 0),
                It.Is<IReadOnlyCollection<PackageIdentityKey>>(retained =>
                    retained.Count == candidates.Count),
                It.Is<IReadOnlyList<IndexRunError>>(errors =>
                    errors.Count == candidates.Count
                    && errors.All(error => error.Code == "package_index_failed")),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _indexStore.Verify(
            store => store.GetIndexedLibrariesAsync(
                DatabasePath,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _packageProcessor.Verify(
            processor => processor.ProcessAsync(
                It.IsAny<PackageVersionCandidate>(),
                It.IsAny<DownloadedPackage>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyDocumentationNotCalled();
        VerifyNoOtherCalls();
    }

    private void SetupCommon(IndexingSettings settings)
    {
        _configurationProvider
            .Setup(provider => provider.GetSettings())
            .Returns(settings);
        _indexStore
            .Setup(store => store.InitializeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _indexStore
            .Setup(store => store.GetIndexedLibrariesAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void VerifyDocumentationNotCalled()
    {
        _documentationReader.Verify(
            reader => reader.ReadAsync(
                It.IsAny<DocumentationSourceDefinition>(),
                It.IsAny<PackageProcessingLimits>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _indexStore.Verify(
            store => store.PublishDocumentationAsync(
                It.IsAny<string>(),
                It.IsAny<DocumentationSourceDefinition>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DocumentationIndexData>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyNoOtherCalls()
    {
        _configurationProvider.VerifyNoOtherCalls();
        _sourceClient.VerifyNoOtherCalls();
        _packageProcessor.VerifyNoOtherCalls();
        _documentationReader.VerifyNoOtherCalls();
        _indexStore.VerifyNoOtherCalls();
    }

    private static IndexingSettings CreateSettings(
        IndexSourceDefinition source) =>
        new(DatabasePath, CreateLimits(), [source]);

    private static IndexSourceDefinition CreateSource(
        IReadOnlyList<PackageSelectionDefinition> packages,
        IReadOnlyList<string> deletedPackageIds) =>
        new(
            "test",
            "test",
            "fixture",
            packages,
            deletedPackageIds,
            10);

    private static PackageProcessingLimits CreateLimits() =>
        new(
            1,
            1,
            1,
            1,
            1,
            1,
            TimeSpan.FromSeconds(1));

    private static IndexPublishResult EmptyPublishResult() =>
        new(0, 0, [], [], []);
}
