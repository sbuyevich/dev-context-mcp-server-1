namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// Root configuration for the Indexer console application.
/// </summary>
public sealed class IndexerOptions
{
    public const string SectionName = "DevContextMcp";

    public string DatabasePath { get; set; } = "data/docs.db";

    public string NugetsPath { get; set; } = "nugets";

    public List<NuGetEnvironmentOptions> Environments { get; set; } = [];

    public DocumentationOptions? Documentation { get; set; }

    public IndexingOptions Indexing { get; set; } = new();
}
