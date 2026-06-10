using DevContextMcp.Indexer.Cli;
using DevContextMcp.Indexer.Services;
using DevContextMcp.Infrastructure.Indexing.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.IntegrationTests.Indexing;

public sealed class NuGetIndexingPipelineTests
{
    [Fact]
    public async Task LocalPackageIsIndexedIntoSqliteAndFtsIdempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-server-tests-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);

        try
        {
            using var provider = CreateProvider(feed, databasePath);
            var coordinator = provider.GetRequiredService<IIndexCoordinator>();

            var first = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));

            Assert.Equal("succeeded", first.Status);
            Assert.Equal(1, first.Discovered);
            Assert.Equal(1, first.Indexed);
            Assert.Equal(1, first.Changed);
            Assert.Equal(0, first.Unchanged);

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(3L, await ScalarAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(
                "test",
                await TextScalarAsync(connection, "SELECT environment FROM sources;"));
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM dependencies;") > 0);
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM target_frameworks;") > 0);
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM symbols;") > 0);
            Assert.True(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM document_chunks_fts WHERE document_chunks_fts MATCH 'fixture';") > 0);

            var second = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));
            Assert.Equal(0, second.Changed);
            Assert.Equal(1, second.Unchanged);
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM index_runs;"));

            FixtureNuGetPackage.ReplaceWithUnsafeArchive(feed);
            var failed = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));

            Assert.Equal("failed", failed.Status);
            Assert.Single(failed.Errors);
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.True(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM document_chunks_fts WHERE document_chunks_fts MATCH 'fixture';") > 0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VersionTwoDatabaseMigratesEnvironmentWithoutChangingSourceId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-migration-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "docs.db");
        Directory.CreateDirectory(root);

        try
        {
            await using (var connection = new SqliteConnection(
                             $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE sources (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        service_index TEXT NOT NULL,
                        last_indexed_at TEXT NULL
                    );
                    INSERT INTO sources (id, name, service_index)
                    VALUES ('stable-source-id', 'qa-feed', 'https://packages.example/v3/index.json');
                    PRAGMA user_version = 2;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var store = new SqliteIndexStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);

            await using var migrated = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await migrated.OpenAsync();
            Assert.Equal(3L, await ScalarAsync(migrated, "PRAGMA user_version;"));
            Assert.Equal(
                "stable-source-id",
                await TextScalarAsync(migrated, "SELECT id FROM sources;"));
            Assert.Equal(
                "qa-feed",
                await TextScalarAsync(migrated, "SELECT environment FROM sources;"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReindexingUpdatesEnvironmentWithoutChangingStableIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-environment-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);

        try
        {
            using (var provider = CreateProvider(feed, databasePath, "qa"))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            string sourceId;
            string libraryId;
            string versionId;
            await using (var connection = new SqliteConnection(
                             $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                sourceId = await TextScalarAsync(connection, "SELECT id FROM sources;");
                libraryId = await TextScalarAsync(connection, "SELECT id FROM libraries;");
                versionId = await TextScalarAsync(connection, "SELECT id FROM library_versions;");
            }

            using (var provider = CreateProvider(feed, databasePath, "production"))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            await using var updated = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await updated.OpenAsync();
            Assert.Equal(sourceId, await TextScalarAsync(updated, "SELECT id FROM sources;"));
            Assert.Equal(libraryId, await TextScalarAsync(updated, "SELECT id FROM libraries;"));
            Assert.Equal(versionId, await TextScalarAsync(updated, "SELECT id FROM library_versions;"));
            Assert.Equal(
                "production",
                await TextScalarAsync(updated, "SELECT environment FROM sources;"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PackageFilesPublishTogetherAndControlPruning()
    {
        const string secondPackageId = "Fixture.Second";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-pruning-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);
        FixtureNuGetPackage.Create(feed, packageId: secondPackageId);
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId,
                MaxVersionsPerPackage: 1),
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                secondPackageId,
                MaxVersionsPerPackage: 1));

        try
        {
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var summary = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
                Assert.Equal(2, summary.Indexed);
            }

            File.Delete(Path.Combine(sourcesPath, $"test.{secondPackageId}.json"));
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(
                1L,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));

            File.Delete(Path.Combine(
                sourcesPath,
                $"test.{FixtureNuGetPackage.PackageId}.json"));
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                Assert.Empty(await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
            }

            Assert.Equal(
                1L,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MultipleFeedsSharingEnvironmentAreEachIndexedOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-shared-env-{Guid.NewGuid():N}");
        var firstFeed = Path.Combine(root, "first");
        var secondFeed = Path.Combine(root, "second");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(firstFeed);
        FixtureNuGetPackage.Create(secondFeed);
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId,
                MaxVersionsPerPackage: 1));

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = databasePath,
                    ["DevContextMcp:NuGetSourcesPath"] = sourcesPath,
                    ["DevContextMcp:Environments:0:Name"] = "first",
                    ["DevContextMcp:Environments:0:Environment"] = "test",
                    ["DevContextMcp:Environments:0:ServiceIndex"] = firstFeed,
                    ["DevContextMcp:Environments:0:MaxPackages"] = "10",
                    ["DevContextMcp:Environments:1:Name"] = "second",
                    ["DevContextMcp:Environments:1:Environment"] = "test",
                    ["DevContextMcp:Environments:1:ServiceIndex"] = secondFeed,
                    ["DevContextMcp:Environments:1:MaxPackages"] = "10",
                    ["DevContextMcp:Indexing:MaxCompressionRatio"] = "10000"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddIndexerCli(configuration);
            using var provider = services.BuildServiceProvider(validateScopes: true);

            var summaries = await provider.GetRequiredService<IIndexCoordinator>()
                .IndexAllAsync(CancellationToken.None);

            Assert.Equal(2, summaries.Count);
            Assert.All(summaries, summary => Assert.Equal(1, summary.Indexed));
            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sources;"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM libraries;"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ServiceProvider CreateProvider(
        string feed,
        string databasePath,
        string environment = "test",
        string? sourcesPath = null)
    {
        var root = Directory.GetParent(feed)!.FullName;
        sourcesPath ??= FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                environment,
                FixtureNuGetPackage.PackageId,
                MaxVersionsPerPackage: 1));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevContextMcp:DatabasePath"] = databasePath,
                ["DevContextMcp:NuGetSourcesPath"] = sourcesPath,
                ["DevContextMcp:Environments:0:Name"] = "fixture",
                ["DevContextMcp:Environments:0:Environment"] = environment,
                ["DevContextMcp:Environments:0:ServiceIndex"] = feed,
                ["DevContextMcp:Environments:0:MaxPackages"] = "10",
                ["DevContextMcp:Indexing:MaxCompressionRatio"] = "10000"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIndexerCli(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> TextScalarAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
