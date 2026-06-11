using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Infrastructure;

public interface IIndexingConfigurationProvider
{
    IndexingSettings GetSettings();
}
