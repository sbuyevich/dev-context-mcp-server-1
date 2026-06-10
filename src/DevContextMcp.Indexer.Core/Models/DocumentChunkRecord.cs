namespace DevContextMcp.Indexer.Core.Models;

public sealed record DocumentChunkRecord(
    string Path,
    string Kind,
    string? MemberName,
    int Ordinal,
    string Content,
    string ContentHash);
