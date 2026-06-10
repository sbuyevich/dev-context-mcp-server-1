namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// Root configuration for the Indexer console application.
/// </summary>
public sealed class IndexerOptions
{
    public const string SectionName = "DevContextMcp";

    public string DatabasePath { get; set; } = "data/docs.db";

    public string NuGetSourcesPath { get; set; } = "nuget-sources";

    public List<NuGetEnvironmentOptions> Environments { get; set; } = [];

    public IndexingOptions Indexing { get; set; } = new();
}
