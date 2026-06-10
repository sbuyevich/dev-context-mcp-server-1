namespace DevContextMcp.Indexer.Core.Models;

public sealed record PackageIndexData(
    string PackageId,
    string Version,
    string ContentHash,
    string? Title,
    string? Description,
    string? Summary,
    string? Authors,
    string? Tags,
    string? ProjectUrl,
    string? RepositoryUrl,
    bool IsListed,
    bool IsPrerelease,
    bool IsDeprecated,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ArtifactRecord> Artifacts,
    IReadOnlyList<DocumentChunkRecord> Documents,
    IReadOnlyList<SymbolRecord> Symbols,
    IReadOnlyList<DependencyRecord> Dependencies,
    IReadOnlyList<TargetFrameworkRecord> TargetFrameworks);
