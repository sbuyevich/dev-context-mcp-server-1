namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexRunSummary(
    string SourceName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int Discovered,
    int Indexed,
    int Changed,
    int Unchanged,
    IReadOnlyList<IndexRunError> Errors);
