using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace DevContextMcp.IntegrationTests.Indexing;

public sealed class IndexerProcessTests
{
    [Fact]
    public async Task IndexerIndexesLocalPackageAndExitsZero()
    {
        var root = CreateRoot();
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);

        try
        {
            var result = await RunIndexerAsync(feed, databasePath);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(databasePath), result.Logs);
            Assert.Contains("Indexed libraries", result.Logs, StringComparison.Ordinal);
            Assert.Contains(
                $"{FixtureNuGetPackage.PackageId} versions (1)",
                result.Logs,
                StringComparison.Ordinal);
            Assert.Contains(
                $"test (1): {FixtureNuGetPackage.Version}",
                result.Logs,
                StringComparison.Ordinal);

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM library_versions
                INNER JOIN libraries ON libraries.id = library_versions.library_id
                WHERE libraries.package_id = $packageId COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$packageId", FixtureNuGetPackage.PackageId);

            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task IndexerReturnsOneWhenPackageFails()
    {
        var root = CreateRoot();
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);
        FixtureNuGetPackage.ReplaceWithUnsafeArchive(feed);

        try
        {
            var result = await RunIndexerAsync(feed, databasePath);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Status: failed", result.Logs, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Indexed libraries", result.Logs, StringComparison.Ordinal);
            Assert.Contains("(none)", result.Logs, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task IndexerReturnsOneWhenIndexingPartiallySucceeds()
    {
        const string unsafePackageId = "Fixture.Unsafe";
        var root = CreateRoot();
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);
        FixtureNuGetPackage.CreateUnsafeArchive(feed, unsafePackageId);

        try
        {
            var result = await RunIndexerAsync(
                feed,
                databasePath,
                [FixtureNuGetPackage.PackageId, unsafePackageId]);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Status: partial_success",
                result.Logs,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async Task<IndexerResult> RunIndexerAsync(
        string feed,
        string databasePath,
        IReadOnlyList<string>? packageIds = null)
    {
        packageIds ??= [FixtureNuGetPackage.PackageId];
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            Directory.GetParent(feed)!.FullName,
            packageIds
                .Select(packageId => new FixtureNuGetConfiguration.PackagePolicy(
                    "test",
                    packageId,
                    MaxVersionsPerPackage: 1))
                .ToArray());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(IndexerAssemblyPath());
        startInfo.ArgumentList.Add($"--DevContextMcp:DatabasePath={databasePath}");
        startInfo.ArgumentList.Add($"--DevContextMcp:NuGetSourcesPath={sourcesPath}");
        startInfo.ArgumentList.Add("--DevContextMcp:Environments:0:Name=test");
        startInfo.ArgumentList.Add(
            $"--DevContextMcp:Environments:0:ServiceIndex={feed}");
        startInfo.ArgumentList.Add("--DevContextMcp:Environments:0:MaxPackages=10");
        startInfo.ArgumentList.Add(
            "--DevContextMcp:Indexing:MaxCompressionRatio=10000");

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var logs = string.Join(
            Environment.NewLine,
            await outputTask,
            await errorTask);

        return new(process.ExitCode, logs);
    }

    private static string CreateRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            $"mcp-doc-server-indexer-tests-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string IndexerAssemblyPath()
    {
        var buildConfiguration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException("Build configuration was not found.");
        return Path.Combine(
            RepositoryRoot(),
            "src",
            "DevContextMcp.Indexer",
            "bin",
            buildConfiguration,
            "net10.0",
            "DevContextMcp.Indexer.dll");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevContextMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed record IndexerResult(int ExitCode, string Logs);
}
