using DevContextMcp.Infrastructure.Indexing.Abstractions;
using DevContextMcp.Indexer.Abstractions;
using DevContextMcp.Indexer.Models;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DevContextMcp.Infrastructure.Indexing.NuGet;

internal sealed class NuGetPackageSourceClient(
    INuGetSourceAuthenticationProvider authenticationProvider,
    IContentHasher contentHasher) : IPackageSourceClient
{
    public async Task<IReadOnlyList<PackageVersionCandidate>> DiscoverAsync(
        IndexSourceDefinition source,
        CancellationToken cancellationToken)
    {
        var repository = CreateRepository(source);
        using var cache = new SourceCacheContext();
        var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(
            cancellationToken);
        var candidates = new List<PackageVersionCandidate>();

        foreach (var package in source.Packages
                     .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var metadata = await metadataResource.GetMetadataAsync(
                package.PackageId,
                package.IncludePrerelease,
                package.IncludeUnlisted,
                cache,
                NullLogger.Instance,
                cancellationToken);

            var selectedMetadata = metadata
                .Where(item => package.IncludePrerelease || !item.Identity.Version.IsPrerelease)
                .Where(item => package.IncludeUnlisted || item.IsListed)
                .OrderByDescending(item => item.Identity.Version, VersionComparer.VersionRelease)
                .Take(package.MaxVersions)
                .ToArray();

            foreach (var item in selectedMetadata)
            {
                var deprecation = await item.GetDeprecationMetadataAsync();
                candidates.Add(new PackageVersionCandidate(
                    item.Identity.Id,
                    item.Identity.Version.ToNormalizedString(),
                    item.IsListed,
                    deprecation is not null,
                    item.Published));
            }
        }

        return candidates
            .GroupBy(
                candidate => (candidate.PackageId, candidate.Version),
                PackageVersionComparer.Instance)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task<DownloadedPackage> DownloadAsync(
        IndexSourceDefinition source,
        PackageVersionCandidate package,
        PackageProcessingLimits limits,
        CancellationToken cancellationToken)
    {
        var repository = CreateRepository(source);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(
            cancellationToken);
        using var cache = new SourceCacheContext();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.PackageDownloadTimeout);

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"mcp-doc-server-{Guid.NewGuid():N}.nupkg");

        try
        {
            long length;
            await using (var file = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81_920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var bounded = new LengthLimitedStream(file, limits.MaxPackageBytes))
            {
                var copied = await resource.CopyNupkgToStreamAsync(
                    package.PackageId,
                    NuGetVersion.Parse(package.Version),
                    bounded,
                    cache,
                    NullLogger.Instance,
                    timeout.Token);

                if (!copied)
                {
                    throw new InvalidDataException(
                        $"NuGet source did not return {package.PackageId} {package.Version}.");
                }

                await bounded.FlushAsync(timeout.Token);
                length = bounded.Length;
            }

            await using var readStream = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await contentHasher.HashAsync(readStream, timeout.Token);
            return new DownloadedPackage(tempPath, hash, length);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private SourceRepository CreateRepository(IndexSourceDefinition source)
    {
        var packageSource = new PackageSource(source.ServiceIndex, source.Name);
        authenticationProvider.Configure(packageSource, source.Name);
        return Repository.Factory.GetCoreV3(packageSource);
    }

    private sealed class PackageVersionComparer :
        IEqualityComparer<(string PackageId, string Version)>
    {
        public static PackageVersionComparer Instance { get; } = new();

        public bool Equals(
            (string PackageId, string Version) x,
            (string PackageId, string Version) y) =>
            string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string PackageId, string Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
    }
}
