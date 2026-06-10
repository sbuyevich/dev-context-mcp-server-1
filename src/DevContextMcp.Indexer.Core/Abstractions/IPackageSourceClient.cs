using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Indexer.Core.Abstractions;

public interface IPackageSourceClient
{
    Task<IReadOnlyList<PackageVersionCandidate>> DiscoverAsync(
        IndexSourceDefinition source,
        CancellationToken cancellationToken);

    Task<DownloadedPackage> DownloadAsync(
        IndexSourceDefinition source,
        PackageVersionCandidate package,
        PackageProcessingLimits limits,
        CancellationToken cancellationToken);
}
