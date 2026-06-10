namespace DevContextMcp.Indexer.Core.Models;

public sealed record PackageProcessingLimits(
    long MaxPackageBytes,
    long MaxDocumentBytes,
    int MaxArchiveEntries,
    long MaxExtractedBytes,
    double MaxCompressionRatio,
    int MaxDocumentChars,
    TimeSpan PackageDownloadTimeout);
