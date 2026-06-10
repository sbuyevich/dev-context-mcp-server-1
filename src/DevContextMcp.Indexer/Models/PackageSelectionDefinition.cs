namespace DevContextMcp.Indexer.Models;

public sealed record PackageSelectionDefinition(
    string PackageId,
    bool IncludePrerelease,
    bool IncludeUnlisted,
    int MaxVersions);
