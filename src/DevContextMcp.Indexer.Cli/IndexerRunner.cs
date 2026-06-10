using DevContextMcp.Indexer.Cli.Configuration;
using DevContextMcp.Indexer.Models;
using DevContextMcp.Indexer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Indexer.Cli;

internal sealed class IndexerRunner(
    IOptions<IndexerOptions> options,
    IIndexCoordinator indexCoordinator,
    ILogger<IndexerRunner> logger)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Environments.Count == 0)
        {
            logger.LogInformation("No NuGet environments are configured; indexing was skipped.");
            return true;
        }

        try
        {
            var summaries = await indexCoordinator.IndexAllAsync(cancellationToken);
            foreach (var summary in summaries)
            {
                LogSummary(summary);
            }

            return summaries.All(summary =>
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

        logger.Log(
            logLevel,
            "NuGet index run completed.\r\nSource: {SourceName}\r\nStatus: {Status}\r\nDiscovered: {Discovered}\r\nIndexed: {Indexed}\r\nChanged: {Changed}\r\nUnchanged: {Unchanged}\r\nErrors: {ErrorCount}",
            summary.SourceName,
            summary.Status,
            summary.Discovered,
            summary.Indexed,
            summary.Changed,
            summary.Unchanged,
            summary.Errors.Count);
    }
}
