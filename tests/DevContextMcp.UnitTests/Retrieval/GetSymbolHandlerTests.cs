using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;
using DevContextMcp.Server.Core.Services;
using Moq;

namespace DevContextMcp.UnitTests.Retrieval;

public sealed class GetSymbolHandlerTests
{
    private const string DatabasePath = "fixture.db";
    private const string LibraryVersionId = "library-version-id";
    private const string PackageId = "Company.Package";
    private const string SourceName = "qa";
    private const string Version = "1.2.3";

    private readonly Mock<ILibraryResolver> _libraryResolver = new();
    private readonly Mock<INuGetReadStore> _store = new();
    private readonly Mock<ICitationFactory> _citationFactory = new();
    private readonly GetSymbolHandler _target;

    public GetSymbolHandlerTests()
    {
        _target = new GetSymbolHandler(
            CreateSettings(),
            _libraryResolver.Object,
            _store.Object,
            _citationFactory.Object);
    }

    // Purpose: rejects symbol lookup for the versionless company documentation library
    [Fact]
    public async Task HandleAsync_DocumentationLibrary_ReturnsSymbolLookupNotSupported()
    {
        // arrange
        var request = new GetSymbolRequest("docs:company-docs", "Company.Widget");

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.NotFound, actual.Status);
        Assert.Contains(actual.Errors, error =>
            error.Code == "symbol_lookup_not_supported");
        VerifyNoDependencyCalls();
    }

    // Purpose: returns a clear error when the requested environment is not indexed
    [Fact]
    public async Task HandleAsync_EnvironmentNotFound_ReturnsNotFound()
    {
        // arrange
        var request = new GetSymbolRequest(
            $"nuget:missing/{PackageId}",
            "Company.Widget");
        _libraryResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<LibraryId>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryResolutionResult(
                LibraryResolutionStatus.EnvironmentNotFound));

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.NotFound, actual.Status);
        Assert.Contains(actual.Errors, error =>
            error.Code == "environment_not_found");
        VerifySettingsAndResolution(request, "missing");
        VerifyStoreNotCalled();
        _citationFactory.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    // Purpose: returns not found when the selected package version contains no matching symbol
    [Fact]
    public async Task HandleAsync_NoSymbolMatches_ReturnsSymbolNotFound()
    {
        // arrange
        var request = new GetSymbolRequest(
            $"nuget:{SourceName}/{PackageId}",
            "Company.Missing",
            Version: Version,
            TargetFramework: "net10.0");
        SetupResolvedLibrary();
        _store
            .Setup(store => store.SearchSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.NotFound, actual.Status);
        Assert.Equal(Version, actual.ResolvedContext!.Version);
        Assert.Contains(actual.Errors, error => error.Code == "symbol_not_found");
        VerifySettingsAndResolution(request, SourceName);
        _store.Verify(
            store => store.SearchSymbolsAsync(
                DatabasePath,
                LibraryVersionId,
                request.Symbol,
                request.TargetFramework,
                12,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _store.Verify(
            store => store.GetRelatedSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _citationFactory.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    // Purpose: returns candidates instead of silently selecting between equally ranked symbols
    [Fact]
    public async Task HandleAsync_AmbiguousWinningTier_ReturnsCandidates()
    {
        // arrange
        var request = new GetSymbolRequest(
            $"nuget:{SourceName}/{PackageId}",
            "Widget");
        var first = CreateSymbol(
            "Company.First.Widget",
            targetFramework: "net8.0",
            matchTier: 2);
        var firstDuplicateFramework = first with { TargetFramework = "net10.0" };
        var second = CreateSymbol(
            "Company.Second.Widget",
            targetFramework: "net10.0",
            matchTier: 2);
        SetupResolvedLibrary();
        _store
            .Setup(store => store.SearchSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, firstDuplicateFramework, second]);
        _citationFactory
            .Setup(factory => factory.SymbolUri(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns<string, string, string, string>(
                (source, package, version, symbol) =>
                    $"nuget://{source}/{package}/{version}/symbol/{symbol}");

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.InsufficientEvidence, actual.Status);
        Assert.Equal(2, actual.Data!.Candidates.Count);
        Assert.Equal(
            ["net10.0", "net8.0"],
            actual.Data.Candidates[0].TargetFrameworks);
        Assert.Contains(actual.Warnings, warning => warning.Code == "ambiguous_symbol");
        VerifySettingsAndResolution(request, SourceName);
        _store.Verify(
            store => store.SearchSymbolsAsync(
                DatabasePath,
                LibraryVersionId,
                request.Symbol,
                null,
                12,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _store.Verify(
            store => store.GetRelatedSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _citationFactory.Verify(
            factory => factory.SymbolUri(
                SourceName,
                PackageId,
                Version,
                It.IsAny<string>()),
            Times.Exactly(2));
        VerifyNoOtherCalls();
    }

    // Purpose: returns complete symbol details, related members, evidence, citations, and warnings
    [Fact]
    public async Task HandleAsync_ExactSymbolMatch_ReturnsCompleteSymbolDetails()
    {
        // arrange
        var request = new GetSymbolRequest(
            $"nuget:{SourceName}/{PackageId}",
            "Company.Widget",
            Version: Version);
        var symbol = CreateSymbol(
            "Company.Widget",
            containingType: "Company.Widget",
            documentation: "Widget documentation.",
            xmlMember: "T:Company.Widget",
            matchTier: 0);
        var related = CreateSymbol(
            "Company.Widget.Run",
            containingType: "Company.Widget",
            matchTier: 1);
        SetupResolvedLibrary(
            deprecated: true,
            warningCodes: ["recommended_version_not_indexed"]);
        _store
            .Setup(store => store.SearchSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([symbol]);
        _store
            .Setup(store => store.GetRelatedSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([related]);
        _citationFactory
            .Setup(factory => factory.SymbolUri(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns("nuget://qa/Company.Package/1.2.3/symbol/Company.Widget");

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.Ok, actual.Status);
        Assert.Equal("Company.Widget", actual.Data!.Symbol!.FullyQualifiedName);
        Assert.Equal("Widget documentation.", actual.Data.Symbol.Documentation);
        Assert.Equal("Company.Widget.Run", Assert.Single(
            actual.Data.Symbol.RelatedMembers).FullyQualifiedName);
        Assert.Contains(actual.Warnings, warning =>
            warning.Code == "recommended_version_not_indexed");
        Assert.Contains(actual.Warnings, warning =>
            warning.Code == "deprecated_version");
        Assert.Equal("T:Company.Widget", Assert.Single(actual.Citations).Location);
        Assert.Contains(
            "Widget documentation.",
            Assert.Single(actual.Evidence).Text,
            StringComparison.Ordinal);
        VerifySettingsAndResolution(request, SourceName);
        _store.Verify(
            store => store.SearchSymbolsAsync(
                DatabasePath,
                LibraryVersionId,
                request.Symbol,
                null,
                12,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _store.Verify(
            store => store.GetRelatedSymbolsAsync(
                DatabasePath,
                LibraryVersionId,
                "Company.Widget",
                "Company.Widget",
                10,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _citationFactory.Verify(
            factory => factory.SymbolUri(
                SourceName,
                PackageId,
                Version,
                "Company.Widget"),
            Times.Once);
        VerifyNoOtherCalls();
    }

    // Purpose: maps an unavailable local index to a structured tool error
    [Fact]
    public async Task HandleAsync_IndexUnavailable_ReturnsIndexUnavailableError()
    {
        // arrange
        var request = new GetSymbolRequest(
            $"nuget:{SourceName}/{PackageId}",
            "Company.Widget");
        _libraryResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<LibraryId>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IndexUnavailableException("missing index"));

        // act
        var actual = await _target.HandleAsync(request, CancellationToken.None);

        // assert
        Assert.Equal(ToolResultStatus.NotFound, actual.Status);
        Assert.Contains(actual.Errors, error => error.Code == "index_unavailable");
        VerifySettingsAndResolution(request, SourceName);
        VerifyStoreNotCalled();
        _citationFactory.VerifyNoOtherCalls();
        VerifyNoOtherCalls();
    }

    private void SetupResolvedLibrary(
        bool deprecated = false,
        IReadOnlyList<string>? warningCodes = null)
    {
        _libraryResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<LibraryId>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResolution(deprecated, warningCodes ?? []));
    }

    private void VerifySettingsAndResolution(
        GetSymbolRequest request,
        string environment)
    {
        _libraryResolver.Verify(
            resolver => resolver.ResolveAsync(
                DatabasePath,
                It.Is<LibraryId>(libraryId =>
                    libraryId.PackageId == PackageId
                    && libraryId.Environment == environment),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(),
                request.Version,
                request.ProjectVersion,
                request.IncludePrerelease,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void VerifyStoreNotCalled()
    {
        _store.Verify(
            store => store.SearchSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _store.Verify(
            store => store.GetRelatedSymbolsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyNoDependencyCalls()
    {
        _libraryResolver.VerifyNoOtherCalls();
        _store.VerifyNoOtherCalls();
        _citationFactory.VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _libraryResolver.VerifyNoOtherCalls();
        _store.VerifyNoOtherCalls();
        _citationFactory.VerifyNoOtherCalls();
    }

    private static RetrievalSettings CreateSettings() =>
        new(
            DatabasePath,
            [SourceName],
            [SourceName],
            new RetrievalLimits(
                8,
                10,
                100_000,
                TimeSpan.FromSeconds(10),
                0.2,
                3));

    private static LibraryResolutionResult CreateResolution(
        bool deprecated,
        IReadOnlyList<string> warningCodes)
    {
        var indexedVersion = new IndexedVersionRecord(
            LibraryVersionId,
            Version,
            true,
            false,
            deprecated,
            null);
        var selection = new ResolvedLibrarySelection(
            new ResolvedLibraryRecord(
                "stored-library-id",
                "nuget",
                PackageId,
                SourceName,
                SourceName,
                PackageId,
                "Fixture package"),
            [indexedVersion],
            new VersionResolution(
                indexedVersion,
                "requested_version",
                warningCodes));
        return new(LibraryResolutionStatus.Resolved, selection);
    }

    private static SymbolHitRecord CreateSymbol(
        string fullyQualifiedName,
        string? containingType = null,
        string? targetFramework = "net10.0",
        string? documentation = null,
        string? xmlMember = null,
        int matchTier = 0) =>
        new(
            fullyQualifiedName,
            "type",
            $"public class {fullyQualifiedName.Split('.').Last()}",
            containingType,
            "lib/net10.0/Company.Package.dll",
            targetFramework,
            xmlMember,
            documentation,
            matchTier);
}
