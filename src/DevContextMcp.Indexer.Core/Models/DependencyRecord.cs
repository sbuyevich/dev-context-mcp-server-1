namespace DevContextMcp.Indexer.Core.Models;

public sealed record DependencyRecord(
    string PackageId,
    string VersionRange,
    string? TargetFramework);
