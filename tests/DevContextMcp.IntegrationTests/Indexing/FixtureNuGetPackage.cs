using System.IO.Compression;
using System.Text;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.IntegrationTests.Indexing;

internal static class FixtureNuGetPackage
{
    public const string PackageId = "Fixture.Documentation";
    public const string Version = "1.2.3";

    public static string Create(
        string feedDirectory,
        string version = Version,
        string? readmeText = null,
        string packageId = PackageId)
    {
        Directory.CreateDirectory(feedDirectory);
        var packagePath = Path.Combine(feedDirectory, $"{packageId}.{version}.nupkg");

        using var file = new FileStream(packagePath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteText(
            archive,
            $"{packageId}.nuspec",
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <title>Fixture Documentation</title>
                <authors>MCP Tests</authors>
                <description>A deterministic fixture package for documentation indexing.</description>
                <summary>Fixture summary for full text search.</summary>
                <tags>fixture documentation indexing</tags>
                <projectUrl>https://example.invalid/fixture</projectUrl>
                <repository type="git" url="https://example.invalid/repository.git" />
                <readme>README.md</readme>
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="Fixture.Dependency" version="[1.0.0, 2.0.0)" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        WriteText(
            archive,
            "README.md",
            readmeText
                ?? $"# Fixture Documentation\n\nVersion {version} explains indexed package behavior.");
        WriteText(
            archive,
            "lib/net10.0/DevContextMcp.Indexer.Core.xml",
            """
            <doc>
              <members>
                <member name="T:DevContextMcp.Indexer.Core.Models.PackageIndexData">
                  <summary>Fixture XML documentation for a public package index record.</summary>
                </member>
              </members>
            </doc>
            """);

        var assemblyPath = typeof(PackageIndexData).Assembly.Location;
        var assemblyEntry = archive.CreateEntry(
            "lib/net10.0/DevContextMcp.Indexer.Core.dll",
            CompressionLevel.NoCompression);
        using var source = File.OpenRead(assemblyPath);
        using var destination = assemblyEntry.Open();
        source.CopyTo(destination);

        return packagePath;
    }

    public static void ReplaceWithUnsafeArchive(string feedDirectory)
    {
        CreateUnsafeArchive(feedDirectory, PackageId);
    }

    public static void CreateUnsafeArchive(
        string feedDirectory,
        string packageId,
        string version = Version)
    {
        Directory.CreateDirectory(feedDirectory);
        var packagePath = Path.Combine(feedDirectory, $"{packageId}.{version}.nupkg");
        using var file = new FileStream(packagePath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteText(
            archive,
            $"{packageId}.nuspec",
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>MCP Tests</authors>
                <description>Unsafe replacement fixture.</description>
              </metadata>
            </package>
            """);
        WriteText(archive, "../outside.txt", "This entry must be rejected.");
    }

    private static void WriteText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
