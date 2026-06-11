namespace DevContextMcp.Server.Core.Models;

public sealed record ResolvedLibrarySelection(
    ResolvedLibraryRecord Library,
    IReadOnlyList<IndexedVersionRecord> Versions,
    VersionResolution? Version);
