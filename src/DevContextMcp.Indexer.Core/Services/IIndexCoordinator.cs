using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Services;

public interface IIndexCoordinator
{
    Task<IndexRunResult> IndexAllAsync(CancellationToken cancellationToken);
}
