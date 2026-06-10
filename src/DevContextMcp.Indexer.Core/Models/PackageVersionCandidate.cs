namespace DevContextMcp.Indexer.Core.Models;

public sealed record PackageVersionCandidate(
    string PackageId,
    string Version,
    bool IsListed,
    bool IsDeprecated,
    DateTimeOffset? PublishedAt);
