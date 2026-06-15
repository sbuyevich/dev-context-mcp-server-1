namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// File-system sources consumed by the Indexer.
/// </summary>
public sealed class IndexerSourceOptions
{
    public string NugetsPath { get; set; } = "nugets";

    public DocumentationOptions? Documents { get; set; }
}
