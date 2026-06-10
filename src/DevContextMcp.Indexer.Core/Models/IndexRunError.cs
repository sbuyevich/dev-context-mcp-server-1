namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexRunError(
    string Code,
    string Message,
    string? PackageId = null,
    string? Version = null);
