namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexPublishResult(
    int Changed,
    int Unchanged,
    IReadOnlyList<PackageIdentityKey> Added,
    IReadOnlyList<PackageIdentityKey> Updated,
    IReadOnlyList<PackageIdentityKey> Deleted);
