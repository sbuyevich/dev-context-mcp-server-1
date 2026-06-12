using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Infrastructure;

public interface IIndexStore
{
    Task InitializeAsync(string databasePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexedLibrary>> GetIndexedLibrariesAsync(
        string databasePath,
        CancellationToken cancellationToken);

    Task<IndexPublishResult> PublishSourceAsync(
        string databasePath,
        IndexSourceDefinition source,
        DateTimeOffset startedAt,
        IReadOnlyList<PackageIndexData> packages,
        IReadOnlyList<IndexRunError> errors,
        CancellationToken cancellationToken);

    Task<IndexPublishResult> PublishDocumentationAsync(
        string databasePath,
        DocumentationSourceDefinition source,
        DateTimeOffset startedAt,
        DocumentationIndexData documentation,
        CancellationToken cancellationToken);
}
