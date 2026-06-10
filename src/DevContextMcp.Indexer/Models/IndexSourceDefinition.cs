namespace DevContextMcp.Indexer.Models;

public sealed record IndexSourceDefinition(
    string Name,
    string Environment,
    string ServiceIndex,
    IReadOnlyList<PackageSelectionDefinition> Packages,
    int MaxPackages);
