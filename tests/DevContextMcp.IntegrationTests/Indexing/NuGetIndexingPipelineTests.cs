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

            var firstResult = await coordinator.IndexAllAsync(CancellationToken.None);
            var first = Assert.Single(firstResult.Summaries);

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
            var firstLibrary = Assert.Single(firstResult.IndexedLibraries);
            Assert.Equal(FixtureNuGetPackage.PackageId, firstLibrary.PackageId);
            var firstEnvironment = Assert.Single(firstLibrary.Environments);
            Assert.Equal("test", firstEnvironment.Environment);
            Assert.Equal([FixtureNuGetPackage.Version], firstEnvironment.Versions);

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(4L, await ScalarAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(
                "test",
                await TextScalarAsync(connection, "SELECT environment FROM sources;"));
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM dependencies;") > 0);
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM target_frameworks;") > 0);
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM symbols;") > 0);
            Assert.True(await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM document_chunks_fts WHERE document_chunks_fts MATCH 'fixture';") > 0);

            var second = Assert.Single(
                (await coordinator.IndexAllAsync(CancellationToken.None)).Summaries);
            Assert.Equal(0, second.Changed);
            Assert.Equal(1, second.Unchanged);
            Assert.Empty(second.Added);
            Assert.Empty(second.Updated);
            Assert.Empty(second.Deleted);
            Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));
            Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM index_runs;"));

            FixtureNuGetPackage.Create(feed, readmeText: "Updated fixture documentation.");
            var updated = Assert.Single(
                (await coordinator.IndexAllAsync(CancellationToken.None)).Summaries);

            Assert.Empty(updated.Added);
            Assert.Equal(
                [new PackageIdentityKey(FixtureNuGetPackage.PackageId, FixtureNuGetPackage.Version)],
                updated.Updated);
            Assert.Empty(updated.Deleted);

            FixtureNuGetPackage.ReplaceWithUnsafeArchive(feed);
            var failedResult = await coordinator.IndexAllAsync(CancellationToken.None);
            var failed = Assert.Single(failedResult.Summaries);

            Assert.Equal("failed", failed.Status);
            Assert.Single(failed.Errors);
            Assert.Empty(failed.Added);
            Assert.Empty(failed.Updated);
            Assert.Empty(failed.Deleted);
            Assert.Single(failedResult.IndexedLibraries);
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
                    CREATE TABLE libraries (
                        id TEXT PRIMARY KEY,
                        source_id TEXT NOT NULL,
                        package_id TEXT NOT NULL,
                        normalized_package_id TEXT NOT NULL
                    );
                    CREATE TABLE artifacts (
                        id TEXT PRIMARY KEY,
                        library_version_id TEXT NOT NULL,
                        path TEXT NOT NULL,
                        kind TEXT NOT NULL,
                        content_hash TEXT NOT NULL,
                        size INTEGER NOT NULL
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
            Assert.Equal(4L, await ScalarAsync(migrated, "PRAGMA user_version;"));
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
    public async Task IndexedLibraryInventoryGroupsCaseInsensitivelyAndSortsVersions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mcp-doc-inventory-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "docs.db");

        try
        {
            var store = new SqliteIndexStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);

            await using (var connection = new SqliteConnection(
                             $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO sources (id, name, environment, service_index)
                    VALUES
                        ('source-a', 'qa-a', 'QA', 'fixture-a'),
                        ('source-b', 'qa-b', 'qa', 'fixture-b'),
                        ('source-c', 'prod', 'prod', 'fixture-c');

                    INSERT INTO libraries (id, source_id, package_id, normalized_package_id)
                    VALUES
                        ('library-a', 'source-a', 'Demo.Cities', 'demo.cities'),
                        ('library-b', 'source-b', 'demo.cities', 'demo.cities'),
                        ('library-c', 'source-c', 'Demo.Cities', 'demo.cities');

                    INSERT INTO library_versions (
                        id, library_id, version, content_hash, is_listed, is_prerelease,
                        is_deprecated, indexed_at)
                    VALUES
                        ('version-a', 'library-a', '1.0.0', 'a', 1, 0, 0, '2026-01-01'),
                        ('version-b', 'library-b', '1.0.0', 'b', 1, 0, 0, '2026-01-01'),
                        ('version-c', 'library-b', '1.0.0-beta.1', 'c', 1, 1, 0, '2026-01-01'),
                        ('version-d', 'library-c', '2.0.0', 'd', 1, 0, 0, '2026-01-01');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var inventory = await store.GetIndexedLibrariesAsync(
                databasePath,
                CancellationToken.None);

            var library = Assert.Single(inventory);
            Assert.Equal("Demo.Cities", library.PackageId);
            Assert.Equal(["prod", "QA"], library.Environments
                .Select(environment => environment.Environment));
            Assert.Equal(["2.0.0"], library.Environments[0].Versions);
            Assert.Equal(
                ["1.0.0", "1.0.0-beta.1"],
                library.Environments[1].Versions);
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
    public async Task RemovingPackageSourceJsonDoesNotDeleteIndexedPackage()
    {
        const string secondPackageId = "Fixture.Second";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-source-removal-{Guid.NewGuid():N}");
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
                var summary = Assert.Single((await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None)).Summaries);
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
                var summary = Assert.Single((await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None)).Summaries);
                Assert.Empty(summary.Deleted);
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(
                2L,
                await ScalarAsync(connection, "SELECT COUNT(*) FROM library_versions;"));

            File.Delete(Path.Combine(
                sourcesPath,
                $"test.{FixtureNuGetPackage.PackageId}.json"));
            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                Assert.Empty((await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None)).Summaries);
            }

            Assert.Equal(
                2L,
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
    public async Task RemovingPackageFileDoesNotDeleteIndexedVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-package-removal-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        var packagePath = FixtureNuGetPackage.Create(feed);

        try
        {
            using (var provider = CreateProvider(feed, databasePath))
            {
                await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
            }

            File.Delete(packagePath);
            using (var provider = CreateProvider(feed, databasePath))
            {
                var result = await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
                var summary = Assert.Single(result.Summaries);

                Assert.Empty(summary.Deleted);
                Assert.Contains(
                    result.IndexedLibraries,
                    library => library.PackageId == FixtureNuGetPackage.PackageId);
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
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
    public async Task ReducingVersionLimitDoesNotDeleteIndexedVersions()
    {
        const string olderVersion = "1.0.0";
        const string newerVersion = "2.0.0";
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-version-limit-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed, version: olderVersion);
        FixtureNuGetPackage.Create(feed, version: newerVersion);
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId,
                MaxVersionsPerPackage: 2));

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
                    MaxVersionsPerPackage: 1));

            using (var provider = CreateProvider(feed, databasePath, sourcesPath: sourcesPath))
            {
                var result = await provider.GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
                var summary = Assert.Single(result.Summaries);
                var library = Assert.Single(result.IndexedLibraries);
                var environment = Assert.Single(library.Environments);

                Assert.Empty(summary.Deleted);
                Assert.Equal(
                    [olderVersion, newerVersion],
                    environment.Versions.Order(StringComparer.Ordinal));
            }

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(
                2L,
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
    public async Task DeleteTombstoneRemovesAllVersionsWithoutDiscoveringOrDeletingOthers()
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
                var summary = Assert.Single((await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None)).Summaries);
                Assert.Equal(2, summary.Discovered);
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
                var result = await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
                var summary = Assert.Single(result.Summaries);

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
                Assert.DoesNotContain(
                    result.IndexedLibraries,
                    library => library.PackageId.Equals(
                        FixtureNuGetPackage.PackageId,
                        StringComparison.OrdinalIgnoreCase));
                Assert.Contains(
                    result.IndexedLibraries,
                    library => library.PackageId.Equals(
                        retainedPackageId,
                        StringComparison.OrdinalIgnoreCase));
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
                var repeated = Assert.Single((await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None)).Summaries);
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
            var summary = Assert.Single((await updatedProvider
                .GetRequiredService<IIndexCoordinator>()
                .IndexAllAsync(CancellationToken.None)).Summaries);

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
                var result = await provider
                    .GetRequiredService<IIndexCoordinator>()
                    .IndexAllAsync(CancellationToken.None);
                var summary = Assert.Single(result.Summaries);
                Assert.Equal(
                    [
                        new PackageIdentityKey(
                            FixtureNuGetPackage.PackageId,
                            FixtureNuGetPackage.Version)
                    ],
                    summary.Deleted);
                var library = Assert.Single(result.IndexedLibraries);
                Assert.Equal(FixtureNuGetPackage.PackageId, library.PackageId);
                var environment = Assert.Single(library.Environments);
                Assert.Equal("qa", environment.Environment);
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
