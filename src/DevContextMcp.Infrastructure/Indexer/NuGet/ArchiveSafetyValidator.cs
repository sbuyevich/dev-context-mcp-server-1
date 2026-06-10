using System.IO.Compression;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Infrastructure.Indexer.NuGet;

internal static class ArchiveSafetyValidator
{
    public static IReadOnlyList<ZipArchiveEntry> Validate(
        ZipArchive archive,
        PackageProcessingLimits limits)
    {
        if (archive.Entries.Count > limits.MaxArchiveEntries)
        {
            throw new InvalidDataException(
                $"Package has {archive.Entries.Count} entries; the configured limit is {limits.MaxArchiveEntries}.");
        }

        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName);

            if (entry.Length < 0 || totalLength > limits.MaxExtractedBytes - entry.Length)
            {
                throw new InvalidDataException(
                    $"Package extracted size exceeds the configured limit of {limits.MaxExtractedBytes} bytes.");
            }

            totalLength += entry.Length;
            if (entry.Length > 0)
            {
                var ratio = entry.CompressedLength == 0
                    ? double.PositiveInfinity
                    : (double)entry.Length / entry.CompressedLength;

                if (ratio > limits.MaxCompressionRatio)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' exceeds the configured compression ratio.");
                }
            }
        }

        return archive.Entries;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.IndexOf('\0') >= 0
            || path.Any(character => char.IsControl(character)))
        {
            throw new InvalidDataException("Package contains an invalid archive entry path.");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidDataException(
                $"Archive entry '{path}' attempts to escape the package root.");
        }
    }
}
