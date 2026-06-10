using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Abstractions;

public interface IIndexingConfigurationProvider
{
    IndexingSettings GetSettings();
}
