using System.IO.Compression;
using DevContextMcp.Infrastructure.Indexing.NuGet;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class ArchiveSafetyValidatorTests
{
    [Fact]
    public void PathTraversalEntryIsRejected()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("unsafe");
        }

        memory.Position = 0;
        using var readArchive = new ZipArchive(memory, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() =>
            ArchiveSafetyValidator.Validate(readArchive, Limits()));

        Assert.Contains("escape", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntryCountLimitIsEnforced()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("one.txt");
            archive.CreateEntry("two.txt");
        }

        memory.Position = 0;
        using var readArchive = new ZipArchive(memory, ZipArchiveMode.Read);
        var limits = Limits() with { MaxArchiveEntries = 1 };

        Assert.Throws<InvalidDataException>(() =>
            ArchiveSafetyValidator.Validate(readArchive, limits));
    }

    [Fact]
    public void PackageDownloadSizeLimitIsEnforced()
    {
        using var inner = new MemoryStream();
        using var bounded = new LengthLimitedStream(inner, 4);

        bounded.Write([1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => bounded.WriteByte(5));
    }

    private static PackageProcessingLimits Limits() => new(
        1_000_000,
        100_000,
        100,
        1_000_000,
        1_000,
        4_000,
        TimeSpan.FromSeconds(10));
}
