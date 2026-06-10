namespace DevContextMcp.Indexer.Core.Models;

public sealed record PackageSelectionDefinition(
    string PackageId,
    bool IncludePrerelease,
    bool IncludeUnlisted,
    int MaxVersions);
