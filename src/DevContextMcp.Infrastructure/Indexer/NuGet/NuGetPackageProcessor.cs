using System.IO.Compression;
using System.Text;
using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;
using NuGet.Packaging;

namespace DevContextMcp.Infrastructure.Indexer.NuGet;

internal sealed class NuGetPackageProcessor(
    IDocumentChunker documentChunker,
    IContentHasher contentHasher) : IPackageProcessor
{
    public async Task<PackageIndexData> ProcessAsync(
        PackageVersionCandidate candidate,
        DownloadedPackage package,
        PackageProcessingLimits limits,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            package.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = ArchiveSafetyValidator.Validate(archive, limits);
        var nuspecEntry = entries.SingleOrDefault(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Package does not contain a .nuspec file.");

        NuspecReader nuspec;
        await using (var nuspecStream = nuspecEntry.Open())
        {
            nuspec = new NuspecReader(nuspecStream);
        }

        var artifacts = new List<ArtifactRecord>();
        var documents = new List<DocumentChunkRecord>();
        var symbols = new List<SymbolRecord>();
        var selectedAssemblies = SelectAssemblies(entries);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDocument(entry))
            {
                var bytes = await ReadEntryAsync(entry, limits.MaxDocumentBytes, cancellationToken);
                var path = NormalizePath(entry.FullName);
                var kind = GetDocumentKind(path);
                var content = DecodeText(bytes);
                artifacts.Add(new(path, kind, contentHasher.Hash(bytes), bytes.LongLength));
                documents.AddRange(documentChunker.Chunk(
                    path,
                    kind,
                    content,
                    limits.MaxDocumentChars));
            }
        }

        foreach (var entry in selectedAssemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadEntryAsync(entry, limits.MaxExtractedBytes, cancellationToken);
            var path = NormalizePath(entry.FullName);
            artifacts.Add(new(path, "managed_assembly", contentHasher.Hash(bytes), bytes.LongLength));
            symbols.AddRange(MetadataSymbolExtractor.Extract(bytes, path));
        }

        var dependencies = nuspec.GetDependencyGroups()
            .SelectMany(group => group.Packages.Select(dependency => new DependencyRecord(
                dependency.Id,
                dependency.VersionRange.ToNormalizedString(),
                group.TargetFramework.IsAny
                    ? null
                    : group.TargetFramework.GetShortFolderName())))
            .ToArray();

        var frameworks = dependencies
            .Select(dependency => dependency.TargetFramework)
            .Concat(selectedAssemblies.Select(entry => GetTargetFramework(entry.FullName)))
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(framework => new TargetFrameworkRecord(framework!))
            .ToArray();

        var repository = nuspec.GetRepositoryMetadata();
        return new PackageIndexData(
            nuspec.GetId(),
            nuspec.GetVersion().ToNormalizedString(),
            package.ContentHash,
            NullIfEmpty(nuspec.GetTitle()),
            NullIfEmpty(nuspec.GetDescription()),
            NullIfEmpty(nuspec.GetSummary()),
            NullIfEmpty(nuspec.GetAuthors()),
            NullIfEmpty(nuspec.GetTags()),
            nuspec.GetProjectUrl()?.ToString(),
            NullIfEmpty(repository?.Url),
            candidate.IsListed,
            nuspec.GetVersion().IsPrerelease,
            candidate.IsDeprecated,
            candidate.PublishedAt,
            artifacts,
            documents,
            symbols,
            dependencies,
            frameworks);
    }

    private static IReadOnlyList<ZipArchiveEntry> SelectAssemblies(
        IReadOnlyList<ZipArchiveEntry> entries)
    {
        var assemblies = entries
            .Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(entry =>
            {
                var path = NormalizePath(entry.FullName);
                return path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        var refNames = assemblies
            .Where(entry => NormalizePath(entry.FullName).StartsWith(
                "ref/",
                StringComparison.OrdinalIgnoreCase))
            .Select(entry => Path.GetFileName(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return assemblies
            .Where(entry =>
                NormalizePath(entry.FullName).StartsWith("ref/", StringComparison.OrdinalIgnoreCase)
                || !refNames.Contains(Path.GetFileName(entry.FullName)))
            .ToArray();
    }

    private static bool IsDocument(ZipArchiveEntry entry)
    {
        if (entry.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var path = NormalizePath(entry.FullName);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase)
            && !path.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDocumentKind(string path)
    {
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "xml_documentation";
        }

        return Path.GetFileName(path).StartsWith("readme", StringComparison.OrdinalIgnoreCase)
            ? "readme"
            : "text_documentation";
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes || entry.Length > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.FullName}' exceeds the configured document/artifact limit.");
        }

        await using var entryStream = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        await entryStream.CopyToAsync(memory, cancellationToken);
        if (memory.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.FullName}' exceeds the configured document/artifact limit.");
        }

        return memory.ToArray();
    }

    private static string DecodeText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string? GetTargetFramework(string path)
    {
        var segments = NormalizePath(path).Split('/');
        return segments.Length >= 3 ? segments[1] : null;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
