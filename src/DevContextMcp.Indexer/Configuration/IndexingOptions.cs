namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// Safety and processing limits for documentation indexing.
/// </summary>
public sealed class IndexingOptions
{
    public long MaxPackageBytes { get; set; } = 100 * 1024 * 1024;

    public long MaxDocumentBytes { get; set; } = 20 * 1024 * 1024;

    public int MaxArchiveEntries { get; set; } = 10_000;

    public long MaxExtractedBytes { get; set; } = 500 * 1024 * 1024;

    public double MaxCompressionRatio { get; set; } = 200;

    public int MaxDocumentChars { get; set; } = 4_000;

    public TimeSpan PackageDownloadTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
