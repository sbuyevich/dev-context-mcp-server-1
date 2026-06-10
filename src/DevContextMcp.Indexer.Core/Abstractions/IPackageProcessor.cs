using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Abstractions;

public interface IPackageProcessor
{
    Task<PackageIndexData> ProcessAsync(
        PackageVersionCandidate candidate,
        DownloadedPackage package,
        PackageProcessingLimits limits,
        CancellationToken cancellationToken);
}
