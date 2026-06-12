namespace DevContextMcp.Indexer.Core.Models;

public sealed record IndexRunResult(
    IReadOnlyList<IndexRunSummary> Summaries,
    IReadOnlyList<IndexedLibrary> IndexedLibraries);
