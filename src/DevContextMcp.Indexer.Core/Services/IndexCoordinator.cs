using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Services;

internal sealed class IndexCoordinator(
    IIndexingConfigurationProvider configurationProvider,
    IPackageSourceClient sourceClient,
    IPackageProcessor packageProcessor,
    IDocumentationSourceReader documentationReader,
    IIndexStore indexStore) : IIndexCoordinator
{
    public async Task<IndexRunResult> IndexAllAsync(
        CancellationToken cancellationToken)
    {
        var settings = configurationProvider.GetSettings();
        await indexStore.InitializeAsync(settings.DatabasePath, cancellationToken);

        var summaries = new List<IndexRunSummary>(
            settings.Sources.Count + (settings.Documentation is null ? 0 : 1));
        IReadOnlyList<string> indexedDocuments = [];
        foreach (var source in settings.Sources)
        {
            summaries.Add(await IndexSourceAsync(settings, source, cancellationToken));
        }

        if (settings.Documentation is not null)
        {
            var documentationResult = await IndexDocumentationAsync(
                settings,
                settings.Documentation,
                cancellationToken);
            summaries.Add(documentationResult.Summary);
            indexedDocuments = documentationResult.Paths;
        }

        var indexedLibraries = await indexStore.GetIndexedLibrariesAsync(
            settings.DatabasePath,
            cancellationToken);

        return new(summaries, indexedLibraries, indexedDocuments);
    }

    private async Task<DocumentationIndexResult> IndexDocumentationAsync(
        IndexingSettings settings,
        DocumentationSourceDefinition source,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var documentation = await documentationReader.ReadAsync(
                source,
                settings.Limits,
                cancellationToken);
            var publish = await indexStore.PublishDocumentationAsync(
                settings.DatabasePath,
                source,
                startedAt,
                documentation,
                cancellationToken);

            return new(
                new(
                    SourceName: "company-docs",
                    Status: "succeeded",
                    StartedAt: startedAt,
                    CompletedAt: DateTimeOffset.UtcNow,
                    Discovered: documentation.Artifacts.Count,
                    Indexed: documentation.Artifacts.Count,
                    Changed: publish.Changed,
                    Unchanged: publish.Unchanged,
                    Added: publish.Added,
                    Updated: publish.Updated,
                    Deleted: publish.Deleted,
                    Errors: [],
                    Environment: "company-docs"),
                documentation.Artifacts
                    .Select(artifact => artifact.Path)
                    .ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = new IndexRunError(
                "documentation_index_failed",
                exception.Message,
                "company-docs",
                null);
            return new(
                new(
                    SourceName: "company-docs",
                    Status: "failed",
                    StartedAt: startedAt,
                    CompletedAt: DateTimeOffset.UtcNow,
                    Discovered: 0,
                    Indexed: 0,
                    Changed: 0,
                    Unchanged: 0,
                    Added: [],
                    Updated: [],
                    Deleted: [],
                    Errors: [error],
                    Environment: "company-docs"),
                []);
        }
    }

    private async Task<IndexRunSummary> IndexSourceAsync(
        IndexingSettings settings,
        IndexSourceDefinition source,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<PackageVersionCandidate> candidates = [];

        try
        {
            if (source.Packages.Count > 0)
            {
                candidates = await sourceClient.DiscoverAsync(source, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var discoveryError = new IndexRunError("source_discovery_failed", exception.Message);
            var completedAt = DateTimeOffset.UtcNow;
            await indexStore.PublishSourceAsync(
                settings.DatabasePath,
                source with { DeletedPackageIds = [] },
                startedAt,
                [],
                [discoveryError],
                cancellationToken);

            return new(
                SourceName: source.Name,
                Environment: source.Environment,
                Status: "failed",
                StartedAt: startedAt,
                CompletedAt: completedAt,
                Discovered: 0,
                Indexed: 0,
                Changed: 0,
                Unchanged: 0,
                Added: [],
                Updated: [],
                Deleted: [],
                Errors: [discoveryError]);
        }

        var indexedPackages = new List<PackageIndexData>(candidates.Count);
        var errors = new List<IndexRunError>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var package = await sourceClient.DownloadAsync(
                    source,
                    candidate,
                    settings.Limits,
                    cancellationToken);

                indexedPackages.Add(await packageProcessor.ProcessAsync(
                    candidate,
                    package,
                    settings.Limits,
                    cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(new(
                    "package_index_failed",
                    exception.Message,
                    candidate.PackageId,
                    candidate.Version));
            }
        }

        var publish = await indexStore.PublishSourceAsync(
            settings.DatabasePath,
            source,
            startedAt,
            indexedPackages,
            errors,
            cancellationToken);

        var status = indexedPackages.Count == 0 && errors.Count > 0
            ? "failed"
            : errors.Count > 0 ? "partial_success" : "succeeded";

        return new(
            SourceName: source.Name,
            Environment: source.Environment,
            Status: status,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Discovered: candidates
                .Select(candidate => candidate.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Indexed: indexedPackages.Count,
            Changed: publish.Changed,
            Unchanged: publish.Unchanged,
            Added: publish.Added,
            Updated: publish.Updated,
            Deleted: publish.Deleted,
            Errors: errors);
    }

    private sealed record DocumentationIndexResult(
        IndexRunSummary Summary,
        IReadOnlyList<string> Paths);
}
