namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexedLibrary(
    string PackageId,
    IReadOnlyList<IndexedLibraryEnvironment> Environments);

public sealed record IndexedLibraryEnvironment(
    string Environment,
    IReadOnlyList<string> Versions);
