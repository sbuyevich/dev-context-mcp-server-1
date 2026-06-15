namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexRunSummary(
    string SourceName,
    string Environment = "",
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int Discovered,
    int Indexed,
    int Changed,
    int Unchanged,
    IReadOnlyList<PackageIdentityKey> Added,
    IReadOnlyList<PackageIdentityKey> Updated,
    IReadOnlyList<PackageIdentityKey> Deleted,
    IReadOnlyList<IndexRunError> Errors);
