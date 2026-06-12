using System.IO.Compression;
using DevContextMcp.Infrastructure.Indexer.NuGet;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class ArchiveSafetyValidatorTests
{
    private readonly Func<
        ZipArchive,
        PackageProcessingLimits,
        IReadOnlyList<ZipArchiveEntry>> _target;

    public ArchiveSafetyValidatorTests()
    {
        _target = ArchiveSafetyValidator.Validate;
    }

    // Purpose: rejects archive entries that attempt to escape the package root
    [Fact]
    public void Validate_PathTraversalEntry_ThrowsInvalidDataException()
    {
        // arrange
        using var archive = CreateArchiveWithContent("../outside.txt", "unsafe");

        // act
        var actual = Assert.Throws<InvalidDataException>(() =>
            _target(archive, CreateLimits()));

        // assert
        Assert.Contains("escape", actual.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: rejects archives whose entry count exceeds the configured limit
    [Fact]
    public void Validate_EntryCountExceedsLimit_ThrowsInvalidDataException()
    {
        // arrange
        using var archive = CreateArchiveEntries("one.txt", "two.txt");
        var limits = CreateLimits() with { MaxArchiveEntries = 1 };

        // act
        var actual = Assert.Throws<InvalidDataException>(() =>
            _target(archive, limits));

        // assert
        Assert.Contains(
            "configured limit",
            actual.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ZipArchive CreateArchiveEntries(params string[] entryNames)
    {
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(
                   memory,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var entryName in entryNames)
            {
                archive.CreateEntry(entryName);
            }
        }

        memory.Position = 0;
        return new ZipArchive(memory, ZipArchiveMode.Read);
    }

    private static ZipArchive CreateArchiveWithContent(
        string entryName,
        string content)
    {
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(
                   memory,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        memory.Position = 0;
        return new ZipArchive(memory, ZipArchiveMode.Read);
    }

    private static PackageProcessingLimits CreateLimits() => new(
        1_000_000,
        100_000,
        100,
        1_000_000,
        1_000,
        4_000,
        TimeSpan.FromSeconds(10));
}

public sealed class LengthLimitedStreamTests : IDisposable
{
    private readonly MemoryStream _inner;
    private readonly LengthLimitedStream _target;

    public LengthLimitedStreamTests()
    {
        _inner = new MemoryStream();
        _target = new LengthLimitedStream(_inner, 4);
    }

    // Purpose: rejects a write that would exceed the configured maximum length
    [Fact]
    public void WriteByte_WriteExceedsMaximumLength_ThrowsInvalidDataException()
    {
        // arrange
        _target.Write([1, 2, 3, 4]);

        // act
        var actual = Assert.Throws<InvalidDataException>(() =>
            _target.WriteByte(5));

        // assert
        Assert.Contains(
            "configured limit",
            actual.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _target.Dispose();
    }
}
