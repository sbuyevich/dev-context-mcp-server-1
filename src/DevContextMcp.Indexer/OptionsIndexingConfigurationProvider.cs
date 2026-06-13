using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Indexer.Core.Models;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Indexer;

internal sealed class OptionsIndexingConfigurationProvider(
    IOptions<IndexerOptions> options,
    INuGetPackageOptionsLoader packageOptionsLoader) : IIndexingConfigurationProvider
{
    public IndexingSettings GetSettings()
    {
        var value = options.Value;
        var limits = value.Indexing;
        var packages = value.Environments.Count == 0
            ? []
            : packageOptionsLoader.Load(value.NugetsPath);

        return new(
            Path.GetFullPath(value.DatabasePath, AppContext.BaseDirectory),
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
                            source.Name,
                            StringComparison.OrdinalIgnoreCase))
                        .Where(package => !package.Delete)
                        .Select(package => new PackageSelectionDefinition(
                            package.PackageId,
                            package.IncludePrerelease,
                            package.IncludeUnlisted,
                            package.MaxVersionsPerPackage))
                        .ToArray(),
                    DeletedPackageIds = packages
                        .Where(package => string.Equals(
                            package.Environment,
                            source.Name,
                            StringComparison.OrdinalIgnoreCase))
                        .Where(package => package.Delete)
                        .Select(package => package.PackageId)
                        .ToArray()
                })
                .Where(item =>
                    item.Packages.Length > 0
                    || item.DeletedPackageIds.Length > 0)
                .Select(item => new IndexSourceDefinition(
                    item.Source.Name,
                    item.Source.Name,
                    ResolveSource(item.Source.ServiceIndex),
                    item.Packages,
                    item.DeletedPackageIds,
                    item.Source.MaxPackages))
                .ToArray(),
            value.Documentation is null
                ? null
                : new DocumentationSourceDefinition(
                    Path.GetFullPath(
                        value.Documentation.RootPath,
                        AppContext.BaseDirectory),
                    value.Documentation.Extensions
                        .Select(NormalizeExtension)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    private static string NormalizeExtension(string extension) =>
        extension.Trim().StartsWith('.')
            ? extension.Trim()
            : $".{extension.Trim()}";

    private static string ResolveSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? source
                : Path.GetFullPath(source);
    }
}
