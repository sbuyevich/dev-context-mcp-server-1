using DevContextMcp.Indexer.Core.Abstractions;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Services;

internal sealed class IndexCoordinator(
    IIndexingConfigurationProvider configurationProvider,
    IPackageSourceClient sourceClient,
    IPackageProcessor packageProcessor,
    IIndexStore indexStore) : IIndexCoordinator
{
    public async Task<IReadOnlyList<IndexRunSummary>> IndexAllAsync(
        CancellationToken cancellationToken)
    {
        var settings = configurationProvider.GetSettings();
        await indexStore.InitializeAsync(settings.DatabasePath, cancellationToken);

        var summaries = new List<IndexRunSummary>(settings.Sources.Count);
        foreach (var source in settings.Sources)
        {
            summaries.Add(await IndexSourceAsync(settings, source, cancellationToken));
        }

        return summaries;
    }

    private async Task<IndexRunSummary> IndexSourceAsync(
        IndexingSettings settings,
        IndexSourceDefinition source,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<PackageVersionCandidate> candidates;

        try
        {
            candidates = await sourceClient.DiscoverAsync(source, cancellationToken);
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
                source,
                startedAt,
                [],
                [],
                [discoveryError],
                false,
                cancellationToken);

            return new(
                source.Name,
                "failed",
                startedAt,
                completedAt,
                0,
                0,
                0,
                0,
                [discoveryError]);
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
            true,
            cancellationToken);

        var status = indexedPackages.Count == 0 && errors.Count > 0
            ? "failed"
            : errors.Count > 0 ? "partial_success" : "succeeded";

        return new(
            source.Name,
            status,
            startedAt,
            DateTimeOffset.UtcNow,
            candidates.Count,
            indexedPackages.Count,
            publish.Changed,
            publish.Unchanged,
            errors);
    }
}
