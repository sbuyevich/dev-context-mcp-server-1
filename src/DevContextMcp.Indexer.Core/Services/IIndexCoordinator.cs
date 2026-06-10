using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Services;

public interface IIndexCoordinator
{
    Task<IReadOnlyList<IndexRunSummary>> IndexAllAsync(CancellationToken cancellationToken);
}
