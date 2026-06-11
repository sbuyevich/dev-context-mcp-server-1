using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
using DevContextMcp.Infrastructure.Indexer.Persistence;
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
            Assert.Equal(
                [new PackageIdentityKey(FixtureNuGetPackage.PackageId, FixtureNuGetPackage.Version)],
                first.Added);
            Assert.Empty(first.Updated);
            Assert.Empty(first.Deleted);

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
            Assert.Empty(second.Added);
            Assert.Empty(second.Updated);
            Assert.Empty(second.Deleted);
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM index_runs;"));

            FixtureNuGetPackage.Create(feed, readmeText: "Updated fixture documentation.");
            var updated = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));

            Assert.Empty(updated.Added);
            Assert.Equal(
                [new PackageIdentityKey(FixtureNuGetPackage.PackageId, FixtureNuGetPackage.Version)],
                updated.Updated);
            Assert.Empty(updated.Deleted);

            FixtureNuGetPackage.ReplaceWithUnsafeArchive(feed);
            var failed = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));

            Assert.Equal("failed", failed.Status);
            Assert.Single(failed.Errors);
            Assert.Empty(failed.Added);
            Assert.Empty(failed.Updated);
            Assert.Empty(failed.Deleted);
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
                Assert.Equal(
                    [
                        new PackageIdentityKey(
                            FixtureNuGetPackage.PackageId,
                            FixtureNuGetPackage.Version),
                        new PackageIdentityKey(
                            secondPackageId,
                            FixtureNuGetPackage.Version)
                    ],
                    summary.Added);
            }

            File.Delete(Path.Combine(sourcesPath, $"test.{secondPackageId}.json"));
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var summary = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
                Assert.Equal(
                    [new PackageIdentityKey(secondPackageId, FixtureNuGetPackage.Version)],
                    summary.Deleted);
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
    public async Task DeleteTombstoneRemovesAllVersionsWithoutDiscoveringOrPruningOthers()
    {
        const string retainedPackageId = "Fixture.Retained";
        const string olderVersion = "1.0.0";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-delete-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed, version: olderVersion);
        FixtureNuGetPackage.Create(feed);
        FixtureNuGetPackage.Create(feed, packageId: retainedPackageId);
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId),
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                retainedPackageId,
                MaxVersionsPerPackage: 1));

        try
        {
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var summary = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
                Assert.Equal(3, summary.Indexed);
            }

            FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "test",
                    FixtureNuGetPackage.PackageId,
                    MaxVersionsPerPackage: 0,
                    Delete: true));
            Directory.Delete(feed, recursive: true);

            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var summary = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));

                Assert.Equal("succeeded", summary.Status);
                Assert.Equal(0, summary.Discovered);
                Assert.Equal(0, summary.Indexed);
                Assert.Equal(0, summary.Changed);
                Assert.Equal(
                    [
                        new PackageIdentityKey(FixtureNuGetPackage.PackageId, olderVersion),
                        new PackageIdentityKey(
                            FixtureNuGetPackage.PackageId,
                            FixtureNuGetPackage.Version)
                    ],
                    summary.Deleted);
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(
                1L,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(
                retainedPackageId,
                await TextScalarAsync(connection, "SELECT package_id FROM libraries;"));
            Assert.Equal(
                0L,
                await ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM libraries_fts WHERE package_id = '{FixtureNuGetPackage.PackageId}';"));

            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var repeated = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
                Assert.Empty(repeated.Deleted);
            }
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
    public async Task ActiveAndDeletePackageEntriesPublishTogether()
    {
        const string deletedPackageId = "Fixture.Deleted";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-mixed-delete-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed);
        FixtureNuGetPackage.Create(feed, packageId: deletedPackageId);
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId,
                MaxVersionsPerPackage: 1),
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                deletedPackageId,
                MaxVersionsPerPackage: 1));

        try
        {
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "test",
                    FixtureNuGetPackage.PackageId,
                    MaxVersionsPerPackage: 1),
                new FixtureNuGetConfiguration.PackagePolicy(
                    "test",
                    deletedPackageId,
                    MaxVersionsPerPackage: 0,
                    Delete: true));

            using var updatedProvider = CreateProvider(
                feed,
                databasePath,
                sourcesPath: sourcesPath);
            var summary = Assert.Single(await updatedProvider
                .GetRequiredService<IIndexCoordinator>()
                .IndexAllAsync(CancellationToken.None));

            Assert.Equal(1, summary.Discovered);
            Assert.Equal(1, summary.Indexed);
            Assert.Equal(
                [new PackageIdentityKey(deletedPackageId, FixtureNuGetPackage.Version)],
                summary.Deleted);
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
    public async Task DeleteTombstoneIsScopedToConfiguredEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-delete-scope-{Guid.NewGuid():N}");
        var qaFeed = Path.Combine(root, "qa-feed");
        var prodFeed = Path.Combine(root, "prod-feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(qaFeed);
        FixtureNuGetPackage.Create(prodFeed);

        try
        {
            var qaSources = FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "qa",
                    FixtureNuGetPackage.PackageId,
                    MaxVersionsPerPackage: 1));
            using (var provider = CreateProvider(
                       qaFeed,
                       databasePath,
                       environment: "qa",
                       sourcesPath: qaSources))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            var prodSources = FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "prod",
                    FixtureNuGetPackage.PackageId,
                    MaxVersionsPerPackage: 1));
            using (var provider = CreateProvider(
                       prodFeed,
                       databasePath,
                       environment: "prod",
                       sourcesPath: prodSources))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "prod",
                    FixtureNuGetPackage.PackageId.ToLowerInvariant(),
                    MaxVersionsPerPackage: 0,
                    Delete: true));
            Directory.Delete(prodFeed, recursive: true);
            using (var provider = CreateProvider(
                       prodFeed,
                       databasePath,
                       environment: "prod",
                       sourcesPath: prodSources))
            {
                var summary = Assert.Single(await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None));
                Assert.Equal(
                    [
                        new PackageIdentityKey(
                            FixtureNuGetPackage.PackageId,
                            FixtureNuGetPackage.Version)
                    ],
                    summary.Deleted);
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(
                1L,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(
                "qa",
                await TextScalarAsync(
                    connection,
                    """
                    SELECT sources.environment
                    FROM libraries
                    INNER JOIN sources ON sources.id = libraries.source_id;
                    """));
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
                ["DevContextMcp:Environments:0:Name"] = environment,
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
