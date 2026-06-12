using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Services;

internal sealed class IndexCoordinator(
    IIndexingConfigurationProvider configurationProvider,
    IPackageSourceClient sourceClient,
    IPackageProcessor packageProcessor,
    IIndexStore indexStore) : IIndexCoordinator
{
    public async Task<IndexRunResult> IndexAllAsync(
        CancellationToken cancellationToken)
    {
        var settings = configurationProvider.GetSettings();
        await indexStore.InitializeAsync(settings.DatabasePath, cancellationToken);

        var summaries = new List<IndexRunSummary>(settings.Sources.Count);
        foreach (var source in settings.Sources)
        {
            summaries.Add(await IndexSourceAsync(settings, source, cancellationToken));
        }

        var indexedLibraries = await indexStore.GetIndexedLibrariesAsync(
            settings.DatabasePath,
            cancellationToken);

        return new(summaries, indexedLibraries);
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
                [],
                [discoveryError],
                false,
                cancellationToken);

            return new(
                SourceName: source.Name,
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
        var retained = candidates
            .Select(candidate => new PackageIdentityKey(candidate.PackageId, candidate.Version))
            .ToArray();

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
            retained,
            errors,
            source.Packages.Count > 0,
            cancellationToken);

        var status = indexedPackages.Count == 0 && errors.Count > 0
            ? "failed"
            : errors.Count > 0 ? "partial_success" : "succeeded";

        return new(
            SourceName: source.Name,
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
}
