using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Abstractions;

public interface IIndexStore
{
    Task InitializeAsync(string databasePath, CancellationToken cancellationToken);

    Task<IndexPublishResult> PublishSourceAsync(
        string databasePath,
        IndexSourceDefinition source,
        DateTimeOffset startedAt,
        IReadOnlyList<PackageIndexData> packages,
        IReadOnlyCollection<PackageIdentityKey> retainedPackages,
        IReadOnlyList<IndexRunError> errors,
        bool pruneMissing,
        CancellationToken cancellationToken);
}
