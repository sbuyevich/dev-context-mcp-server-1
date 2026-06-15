using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Indexer;

internal sealed class IndexerRunner(
    IOptions<IndexerOptions> options,
    IIndexCoordinator indexCoordinator,
    ILogger<IndexerRunner> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (options.Value.NugetPackages.Count == 0
            && options.Value.IndexerSource.Documentations is null)
        {
            logger.LogInformation("No indexing sources are configured; indexing was skipped.");
            return true;
        }

        try
        {
            var result = await indexCoordinator.IndexAllAsync(cancellationToken);
            var changedSummaries = result.Summaries.Where(summary =>
                !summary.Status.Equals("succeeded", StringComparison.Ordinal)
                ||
                summary.Added.Count > 0 ||
                summary.Updated.Count > 0 ||
                summary.Deleted.Count > 0);
            
            foreach (var summary in changedSummaries)
            {
                LogSummary(summary);
            }

            LogIndexedDocuments(result.IndexedDocuments);
            LogIndexedLibraries(result.IndexedLibraries);

            return result.Summaries.All(summary =>
                summary.Status.Equals("succeeded", StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "NuGet indexing failed.");
            return false;
        }
    }

    private void LogSummary(IndexRunSummary summary)
    {
        var logLevel = summary.Status switch
        {
            "succeeded" => LogLevel.Information,
            "partial_success" => LogLevel.Warning,
            _ => LogLevel.Error
        };
        var report = FormatSummary(summary);
        logger.Log(logLevel, "{IndexerReport}", report);
    }

    private static string FormatSummary(IndexRunSummary summary) =>
        $@"
        Environment: {(!string.IsNullOrWhiteSpace(summary.Environment) ? summary.Environment : summary.SourceName)}
        Status: {summary.Status}
        NuGets
            Total: {summary.Discovered}
            Indexed: {summary.Indexed}
            Errors: {summary.Errors.Count}
            Added ({summary.Added.Count}): {FormatPackages(summary.Added)}
            Updated ({summary.Updated.Count}): {FormatPackages(summary.Updated)}
            Deleted ({summary.Deleted.Count}):{FormatPackages(summary.Deleted)}
        ";

    private static string FormatPackages(
        IReadOnlyList<PackageIdentityKey> packages) =>
        packages.Count == 0
            ? ""
            : string.Join(
                "; ",
                packages
                    .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(package => package.PackageId, StringComparer.Ordinal)
                    .ThenBy(package => package.Version, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(package => package.Version, StringComparer.Ordinal)
                    .Select(package => $"        {package.PackageId} {package.Version}"));
     

    private void LogIndexedLibraries(IReadOnlyList<IndexedLibrary> libraries)
    {
        var blocks = libraries.Select(library =>
            $"{library.PackageId} versions ({library.Environments.Sum(environment => environment.Versions.Count)})" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                library.Environments.Select(environment =>
                    $"    {environment.Environment} ({environment.Versions.Count}): " +
                    string.Join(", ", environment.Versions))));

        var report = $"{Environment.NewLine}Indexed libraries{Environment.NewLine}{Environment.NewLine}" +
            (libraries.Count == 0
                ? "(none)"
                : string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks));
        logger.LogInformation("{IndexedLibraryReport}", report += $"{Environment.NewLine}-----------------------------------------------------------------------------");
    }

    private void LogIndexedDocuments(IReadOnlyList<string> documents)
    {
        var paths = documents
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal);
        var report = $"{Environment.NewLine}Indexed documents ({documents.Count})" +
            Environment.NewLine +
            Environment.NewLine +
            (documents.Count == 0
                ? "(none)"
                : string.Join(
                    Environment.NewLine,
                    paths.Select(path => $"    {path}")));

        logger.LogInformation("{IndexedDocumentReport}", report);
    }
}
