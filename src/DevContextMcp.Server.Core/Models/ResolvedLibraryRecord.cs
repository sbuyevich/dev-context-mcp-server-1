namespace DevContextMcp.Server.Core.Models;

public sealed record ResolvedLibraryRecord(
    string LibraryId,
    string SourceName,
    string Environment,
    string PackageId,
    string? Description);
