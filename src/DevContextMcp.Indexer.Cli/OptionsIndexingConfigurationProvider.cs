using DevContextMcp.Indexer.Abstractions;
using DevContextMcp.Indexer.Cli.Configuration;
using DevContextMcp.Indexer.Models;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Indexer.Cli;

internal sealed class OptionsIndexingConfigurationProvider(
    IOptions<IndexerOptions> options,
    INuGetPackageOptionsLoader packageOptionsLoader) : IIndexingConfigurationProvider
{
    public IndexingSettings GetSettings()
    {
        var value = options.Value;
        var limits = value.Indexing;
        var packages = packageOptionsLoader.Load(value.NuGetSourcesPath);

        return new(
            value.DatabasePath,
            new PackageProcessingLimits(
                limits.MaxPackageBytes,
                limits.MaxDocumentBytes,
                limits.MaxArchiveEntries,
                limits.MaxExtractedBytes,
                limits.MaxCompressionRatio,
                limits.MaxDocumentChars,
                limits.PackageDownloadTimeout),
            value.Environments
                .Select(source => new
                {
                    Source = source,
                    Packages = packages
                        .Where(package => string.Equals(
                            package.Environment,
                            source.Environment,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(package => new PackageSelectionDefinition(
                            package.PackageId,
                            package.IncludePrerelease,
                            package.IncludeUnlisted,
                            package.MaxVersionsPerPackage))
                        .ToArray()
                })
                .Where(item => item.Packages.Length > 0)
                .Select(item => new IndexSourceDefinition(
                    item.Source.Name,
                    item.Source.Environment,
                    ResolveSource(item.Source.ServiceIndex),
                    item.Packages,
                    item.Source.MaxPackages))
                .ToArray());
    }

    private static string ResolveSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? source
                : Path.GetFullPath(source);
    }
}
