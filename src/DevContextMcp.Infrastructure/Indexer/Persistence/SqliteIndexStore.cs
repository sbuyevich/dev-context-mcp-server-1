using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;
using Microsoft.Data.Sqlite;
using NuGet.Versioning;

namespace DevContextMcp.Infrastructure.Indexer.Persistence;

internal sealed class SqliteIndexStore : IIndexStore
{
    private const int SchemaVersion = 4;
    private const string DocumentationLibraryId = "company-docs";
    private const string DocumentationDisplayName = "Company Docs";
    private const string DocumentationVersion = "current";

    public async Task<IReadOnlyList<IndexedLibrary>> GetIndexedLibrariesAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveDatabasePath(databasePath);
        await using var connection = CreateConnection(resolvedPath);
        await connection.OpenAsync(cancellationToken);

        var rows = new List<IndexedLibraryRow>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.package_id, s.environment, lv.version
            FROM library_versions lv
            INNER JOIN libraries l ON l.id = lv.library_id
            INNER JOIN sources s ON s.id = l.source_id
            WHERE l.kind = 'nuget';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows
            .GroupBy(row => row.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(packageGroup => new IndexedLibrary(
                SelectStoredCasing(packageGroup.Select(row => row.PackageId)),
                packageGroup
                    .GroupBy(row => row.Environment, StringComparer.OrdinalIgnoreCase)
                    .Select(environmentGroup => new IndexedLibraryEnvironment(
                        SelectStoredCasing(environmentGroup.Select(row => row.Environment)),
                        environmentGroup
                            .Select(row => row.Version)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(version => NuGetVersion.Parse(version))
                            .ThenBy(version => version, StringComparer.Ordinal)
                            .ToArray()))
                    .OrderBy(environment => environment.Environment, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(environment => environment.Environment, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(library => library.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(library => library.PackageId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task InitializeAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveDatabasePath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);

        await using var connection = CreateConnection(resolvedPath);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken);

        var version = Convert.ToInt32(
            await ExecuteScalarAsync(connection, null, "PRAGMA user_version;", cancellationToken),
            CultureInfo.InvariantCulture);

        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than supported version {SchemaVersion}.");
        }

        if (version == 0)
        {
            await using var transaction = connection.BeginTransaction();
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                SchemaSql,
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {SchemaVersion};",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else if (version < SchemaVersion)
        {
            await using var transaction = connection.BeginTransaction();
            if (version == 1)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    MigrationV2Sql,
                    cancellationToken);
                await RefreshAllLibrarySearchAsync(
                    connection,
                    transaction,
                    cancellationToken);
            }

            if (version <= 2)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    MigrationV3Sql,
                    cancellationToken);
            }

            if (version <= 3)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    MigrationV4Sql,
                    cancellationToken);
            }

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"PRAGMA user_version = {SchemaVersion};",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<IndexPublishResult> PublishSourceAsync(
        string databasePath,
        IndexSourceDefinition source,
        DateTimeOffset startedAt,
        IReadOnlyList<PackageIndexData> packages,
        IReadOnlyList<IndexRunError> errors,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveDatabasePath(databasePath);
        await using var connection = CreateConnection(resolvedPath);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var sourceId = StableId(source.Name, source.ServiceIndex);
        await UpsertSourceAsync(connection, transaction, sourceId, source, cancellationToken);

        var added = new List<PackageIdentityKey>();
        var updated = new List<PackageIdentityKey>();
        var unchanged = 0;
        foreach (var package in packages)
        {
            var identity = new PackageIdentityKey(package.PackageId, package.Version);
            var libraryId = StableId(sourceId, identity.NormalizedPackageId);
            var versionId = identity.ToStableId(sourceId);
            var existingHash = await GetContentHashAsync(
                connection,
                transaction,
                versionId,
                cancellationToken);

            if (string.Equals(existingHash, package.ContentHash, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            if (existingHash is null)
            {
                added.Add(identity);
            }
            else
            {
                updated.Add(identity);
            }

            await DeleteVersionAsync(connection, transaction, versionId, cancellationToken);
            await UpsertLibraryAsync(
                connection,
                transaction,
                libraryId,
                sourceId,
                package.PackageId,
                cancellationToken);
            await InsertPackageAsync(
                connection,
                transaction,
                sourceId,
                libraryId,
                versionId,
                package,
                cancellationToken);
        }

        var deleted = new List<PackageIdentityKey>();
        deleted.AddRange(await DeleteConfiguredPackagesAsync(
            connection,
            transaction,
            sourceId,
            source.DeletedPackageIds,
            cancellationToken));

        await RefreshSourceLibrarySearchAsync(
            connection,
            transaction,
            sourceId,
            cancellationToken);

        var completedAt = DateTimeOffset.UtcNow;
        var changed = added.Count + updated.Count;
        var status = packages.Count == 0 && errors.Count > 0
            ? "failed"
            : errors.Count > 0 ? "partial_success" : "succeeded";
        var runId = StableId(
            sourceId,
            startedAt.ToString("O", CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"));

        await InsertRunAsync(
            connection,
            transaction,
            runId,
            sourceId,
            status,
            startedAt,
            completedAt,
            packages.Count,
            changed,
            unchanged,
            errors,
            cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE sources
            SET last_indexed_at = $lastIndexedAt
            WHERE id = $sourceId;
            """,
            [("$lastIndexedAt", completedAt.ToString("O")), ("$sourceId", sourceId)],
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new(
            changed,
            unchanged,
            SortIdentities(added),
            SortIdentities(updated),
            SortIdentities(deleted));
    }

    public async Task<IndexPublishResult> PublishDocumentationAsync(
        string databasePath,
        DocumentationSourceDefinition source,
        DateTimeOffset startedAt,
        DocumentationIndexData documentation,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveDatabasePath(databasePath);
        await using var connection = CreateConnection(resolvedPath);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            null,
            "PRAGMA foreign_keys = ON;",
            cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var sourceId = StableId("docs", DocumentationLibraryId);
        var libraryId = StableId(sourceId, DocumentationLibraryId);
        var versionId = StableId(sourceId, DocumentationLibraryId, DocumentationVersion);
        var existingHash = await GetContentHashAsync(
            connection,
            transaction,
            versionId,
            cancellationToken);
        var identity = new PackageIdentityKey(
            DocumentationLibraryId,
            DocumentationVersion);

        await UpsertSourceAsync(
            connection,
            transaction,
            sourceId,
            DocumentationLibraryId,
            string.Empty,
            source.RootPath,
            "docs",
            cancellationToken);
        await UpsertLibraryAsync(
            connection,
            transaction,
            libraryId,
            sourceId,
            DocumentationLibraryId,
            "docs",
            DocumentationDisplayName,
            cancellationToken);

        var changed = 0;
        var unchanged = 0;
        IReadOnlyList<PackageIdentityKey> added = [];
        IReadOnlyList<PackageIdentityKey> updated = [];
        if (string.Equals(existingHash, documentation.ContentHash, StringComparison.Ordinal))
        {
            unchanged = 1;
        }
        else
        {
            if (existingHash is null)
            {
                added = [identity];
            }
            else
            {
                updated = [identity];
            }

            changed = 1;
            await DeleteVersionAsync(connection, transaction, versionId, cancellationToken);
            await InsertPackageAsync(
                connection,
                transaction,
                sourceId,
                libraryId,
                versionId,
                new PackageIndexData(
                    DocumentationLibraryId,
                    DocumentationVersion,
                    documentation.ContentHash,
                    DocumentationDisplayName,
                    "Internal company documentation and standards.",
                    null,
                    null,
                    "company documentation standards",
                    null,
                    null,
                    true,
                    false,
                    false,
                    null,
                    documentation.Artifacts,
                    documentation.Documents,
                    [],
                    [],
                    []),
                cancellationToken);
        }

        await RefreshSourceLibrarySearchAsync(
            connection,
            transaction,
            sourceId,
            cancellationToken);
        var completedAt = DateTimeOffset.UtcNow;
        await InsertRunAsync(
            connection,
            transaction,
            StableId(
                sourceId,
                startedAt.ToString("O", CultureInfo.InvariantCulture),
                Guid.NewGuid().ToString("N")),
            sourceId,
            "succeeded",
            startedAt,
            completedAt,
            documentation.Artifacts.Count,
            changed,
            unchanged,
            [],
            cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE sources
            SET last_indexed_at = $lastIndexedAt
            WHERE id = $sourceId;
            """,
            [("$lastIndexedAt", completedAt.ToString("O")), ("$sourceId", sourceId)],
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new(changed, unchanged, added, updated, []);
    }

    private static async Task InsertPackageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        string libraryId,
        string versionId,
        PackageIndexData package,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO library_versions (
                id, library_id, version, content_hash, title, description, summary,
                authors, tags, project_url, repository_url, is_listed, is_prerelease,
                is_deprecated, published_at, indexed_at)
            VALUES (
                $id, $libraryId, $version, $contentHash, $title, $description, $summary,
                $authors, $tags, $projectUrl, $repositoryUrl, $isListed, $isPrerelease,
                $isDeprecated, $publishedAt, $indexedAt);
            """,
            [
                ("$id", versionId),
                ("$libraryId", libraryId),
                ("$version", package.Version),
                ("$contentHash", package.ContentHash),
                ("$title", package.Title),
                ("$description", package.Description),
                ("$summary", package.Summary),
                ("$authors", package.Authors),
                ("$tags", package.Tags),
                ("$projectUrl", package.ProjectUrl),
                ("$repositoryUrl", package.RepositoryUrl),
                ("$isListed", package.IsListed ? 1 : 0),
                ("$isPrerelease", package.IsPrerelease ? 1 : 0),
                ("$isDeprecated", package.IsDeprecated ? 1 : 0),
                ("$publishedAt", package.PublishedAt?.ToString("O")),
                ("$indexedAt", DateTimeOffset.UtcNow.ToString("O"))
            ],
            cancellationToken);

        var artifactIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in package.Artifacts)
        {
            var artifactId = StableId(versionId, artifact.Path);
            artifactIds[artifact.Path] = artifactId;
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO artifacts (
                    id, library_version_id, path, kind, content_hash, size, content)
                VALUES (
                    $id, $versionId, $path, $kind, $contentHash, $size, $content);
                """,
                [
                    ("$id", artifactId),
                    ("$versionId", versionId),
                    ("$path", artifact.Path),
                    ("$kind", artifact.Kind),
                    ("$contentHash", artifact.ContentHash),
                    ("$size", artifact.Size),
                    ("$content", artifact.Content)
                ],
                cancellationToken);
        }

        foreach (var document in package.Documents)
        {
            var documentId = StableId(
                versionId,
                document.Path,
                document.MemberName ?? string.Empty,
                document.Ordinal.ToString(CultureInfo.InvariantCulture));
            artifactIds.TryGetValue(document.Path, out var artifactId);

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO document_chunks (
                    id, library_version_id, artifact_id, path, kind, member_name,
                    ordinal, content, content_hash)
                VALUES (
                    $id, $versionId, $artifactId, $path, $kind, $memberName,
                    $ordinal, $content, $contentHash);
                """,
                [
                    ("$id", documentId),
                    ("$versionId", versionId),
                    ("$artifactId", artifactId),
                    ("$path", document.Path),
                    ("$kind", document.Kind),
                    ("$memberName", document.MemberName),
                    ("$ordinal", document.Ordinal),
                    ("$content", document.Content),
                    ("$contentHash", document.ContentHash)
                ],
                cancellationToken);

            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO document_chunks_fts (
                    document_chunk_id, package_id, version, path, member_name, content)
                VALUES (
                    $documentId, $packageId, $version, $path, $memberName, $content);
                """,
                [
                    ("$documentId", documentId),
                    ("$packageId", package.PackageId),
                    ("$version", package.Version),
                    ("$path", document.Path),
                    ("$memberName", document.MemberName),
                    ("$content", document.Content)
                ],
                cancellationToken);
        }

        foreach (var symbol in package.Symbols)
        {
            var symbolId = StableId(
                versionId,
                symbol.AssemblyPath,
                symbol.Kind,
                symbol.FullyQualifiedName,
                symbol.Signature);
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO symbols (
                    id, library_version_id, namespace, fully_qualified_name, kind,
                    signature, containing_type, assembly_path, target_framework,
                    xml_documentation_member)
                VALUES (
                    $id, $versionId, $namespace, $fullyQualifiedName, $kind,
                    $signature, $containingType, $assemblyPath, $targetFramework,
                    $xmlDocumentationMember);
                """,
                [
                    ("$id", symbolId),
                    ("$versionId", versionId),
                    ("$namespace", symbol.Namespace),
                    ("$fullyQualifiedName", symbol.FullyQualifiedName),
                    ("$kind", symbol.Kind),
                    ("$signature", symbol.Signature),
                    ("$containingType", symbol.ContainingType),
                    ("$assemblyPath", symbol.AssemblyPath),
                    ("$targetFramework", symbol.TargetFramework),
                    ("$xmlDocumentationMember", symbol.XmlDocumentationMember)
                ],
                cancellationToken);
        }

        foreach (var dependency in package.Dependencies)
        {
            var dependencyId = StableId(
                versionId,
                dependency.PackageId,
                dependency.VersionRange,
                dependency.TargetFramework ?? string.Empty);
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO dependencies (
                    id, library_version_id, package_id, version_range, target_framework)
                VALUES ($id, $versionId, $packageId, $versionRange, $targetFramework);
                """,
                [
                    ("$id", dependencyId),
                    ("$versionId", versionId),
                    ("$packageId", dependency.PackageId),
                    ("$versionRange", dependency.VersionRange),
                    ("$targetFramework", dependency.TargetFramework)
                ],
                cancellationToken);
        }

        foreach (var framework in package.TargetFrameworks)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO target_frameworks (library_version_id, framework)
                VALUES ($versionId, $framework);
                """,
                [("$versionId", versionId), ("$framework", framework.Framework)],
                cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<PackageIdentityKey>> DeleteConfiguredPackagesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        IReadOnlyList<string> packageIds,
        CancellationToken cancellationToken)
    {
        var deleted = new List<PackageIdentityKey>();

        foreach (var packageId in packageIds)
        {
            var library = await GetLibraryAsync(
                connection,
                transaction,
                sourceId,
                packageId,
                cancellationToken);
            if (library is null)
            {
                continue;
            }

            var versions = await GetLibraryVersionsAsync(
                connection,
                transaction,
                library.Value.LibraryId,
                cancellationToken);
            foreach (var version in versions)
            {
                await DeleteVersionAsync(
                    connection,
                    transaction,
                    version.VersionId,
                    cancellationToken);
                deleted.Add(new(library.Value.PackageId, version.Version));
            }

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM libraries_fts WHERE library_id = $libraryId;",
                [("$libraryId", library.Value.LibraryId)],
                cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM libraries WHERE id = $libraryId;",
                [("$libraryId", library.Value.LibraryId)],
                cancellationToken);
        }

        return deleted;
    }

    private static async Task<(string LibraryId, string PackageId)?> GetLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        string packageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, package_id
            FROM libraries
            WHERE source_id = $sourceId
                AND normalized_package_id = $packageId;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue(
            "$packageId",
            packageId.Trim().ToLowerInvariant());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task<IReadOnlyList<(string VersionId, string Version)>>
        GetLibraryVersionsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string libraryId,
            CancellationToken cancellationToken)
    {
        var versions = new List<(string VersionId, string Version)>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, version
            FROM library_versions
            WHERE library_id = $libraryId;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add((reader.GetString(0), reader.GetString(1)));
        }

        return versions;
    }

    private static IReadOnlyList<PackageIdentityKey> SortIdentities(
        IEnumerable<PackageIdentityKey> identities) =>
        identities
            .OrderBy(identity => identity.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(identity => identity.PackageId, StringComparer.Ordinal)
            .ThenBy(identity => identity.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(identity => identity.Version, StringComparer.Ordinal)
            .ToArray();

    private static string SelectStoredCasing(IEnumerable<string> values) =>
        values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .First();

    private static async Task DeleteVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string versionId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM document_chunks_fts
            WHERE document_chunk_id IN (
                SELECT id FROM document_chunks WHERE library_version_id = $versionId
            );
            """,
            [("$versionId", versionId)],
            cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM library_versions WHERE id = $versionId;",
            [("$versionId", versionId)],
            cancellationToken);
    }

    private static async Task<string?> GetContentHashAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string versionId,
        CancellationToken cancellationToken)
    {
        var value = await ExecuteScalarAsync(
            connection,
            transaction,
            "SELECT content_hash FROM library_versions WHERE id = $versionId;",
            cancellationToken,
            [("$versionId", versionId)]);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static Task UpsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        IndexSourceDefinition source,
        CancellationToken cancellationToken) =>
        UpsertSourceAsync(
            connection,
            transaction,
            sourceId,
            source.Name,
            source.Environment,
            source.ServiceIndex,
            "nuget",
            cancellationToken);

    private static Task UpsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        string name,
        string environment,
        string serviceIndex,
        string kind,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO sources (id, name, environment, service_index, kind)
            VALUES ($id, $name, $environment, $serviceIndex, $kind)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                environment = excluded.environment,
                service_index = excluded.service_index,
                kind = excluded.kind;
            """,
            [
                ("$id", sourceId),
                ("$name", name),
                ("$environment", environment),
                ("$serviceIndex", serviceIndex),
                ("$kind", kind)
            ],
            cancellationToken);

    private static Task UpsertLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryId,
        string sourceId,
        string packageId,
        CancellationToken cancellationToken) =>
        UpsertLibraryAsync(
            connection,
            transaction,
            libraryId,
            sourceId,
            packageId,
            "nuget",
            packageId,
            cancellationToken);

    private static Task UpsertLibraryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string libraryId,
        string sourceId,
        string packageId,
        string kind,
        string displayName,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO libraries (
                id, source_id, package_id, normalized_package_id, kind, display_name)
            VALUES (
                $id, $sourceId, $packageId, $normalizedPackageId, $kind, $displayName)
            ON CONFLICT(id) DO UPDATE SET
                package_id = excluded.package_id,
                kind = excluded.kind,
                display_name = excluded.display_name;
            """,
            [
                ("$id", libraryId),
                ("$sourceId", sourceId),
                ("$packageId", packageId),
                ("$normalizedPackageId", packageId.Trim().ToLowerInvariant()),
                ("$kind", kind),
                ("$displayName", displayName)
            ],
            cancellationToken);

    private static async Task InsertRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string sourceId,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int indexed,
        int changed,
        int unchanged,
        IReadOnlyList<IndexRunError> errors,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO index_runs (
                id, source_id, status, started_at, completed_at, duration_ms,
                indexed_count, changed_count, unchanged_count, error_count)
            VALUES (
                $id, $sourceId, $status, $startedAt, $completedAt, $durationMs,
                $indexed, $changed, $unchanged, $errorCount);
            """,
            [
                ("$id", runId),
                ("$sourceId", sourceId),
                ("$status", status),
                ("$startedAt", startedAt.ToString("O")),
                ("$completedAt", completedAt.ToString("O")),
                ("$durationMs", (long)(completedAt - startedAt).TotalMilliseconds),
                ("$indexed", indexed),
                ("$changed", changed),
                ("$unchanged", unchanged),
                ("$errorCount", errors.Count)
            ],
            cancellationToken);

        for (var index = 0; index < errors.Count; index++)
        {
            var error = errors[index];
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO index_run_errors (
                    id, index_run_id, code, message, package_id, version)
                VALUES ($id, $runId, $code, $message, $packageId, $version);
                """,
                [
                    ("$id", StableId(runId, index.ToString(CultureInfo.InvariantCulture))),
                    ("$runId", runId),
                    ("$code", error.Code),
                    ("$message", error.Message),
                    ("$packageId", error.PackageId),
                    ("$version", error.Version)
                ],
                cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        IReadOnlyList<(string Name, object? Value)>? parameters = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static SqliteConnection CreateConnection(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        };
        return new SqliteConnection(builder.ToString());
    }

    private static string ResolveDatabasePath(string databasePath)
    {
        var path = Path.GetFullPath(databasePath);
        return path;
    }

    private static string StableId(params string[] values)
    {
        var value = string.Join('\n', values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record IndexedLibraryRow(
        string PackageId,
        string Environment,
        string Version);

    private const string SchemaSql =
        """
        CREATE TABLE sources (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            environment TEXT NOT NULL,
            service_index TEXT NOT NULL,
            kind TEXT NOT NULL DEFAULT 'nuget',
            last_indexed_at TEXT NULL
        );

        CREATE TABLE libraries (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            package_id TEXT NOT NULL,
            normalized_package_id TEXT NOT NULL,
            kind TEXT NOT NULL DEFAULT 'nuget',
            display_name TEXT NULL,
            UNIQUE(source_id, normalized_package_id)
        );

        CREATE TABLE library_versions (
            id TEXT PRIMARY KEY,
            library_id TEXT NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
            version TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            title TEXT NULL,
            description TEXT NULL,
            summary TEXT NULL,
            authors TEXT NULL,
            tags TEXT NULL,
            project_url TEXT NULL,
            repository_url TEXT NULL,
            is_listed INTEGER NOT NULL,
            is_prerelease INTEGER NOT NULL,
            is_deprecated INTEGER NOT NULL,
            published_at TEXT NULL,
            indexed_at TEXT NOT NULL,
            UNIQUE(library_id, version)
        );

        CREATE TABLE artifacts (
            id TEXT PRIMARY KEY,
            library_version_id TEXT NOT NULL REFERENCES library_versions(id) ON DELETE CASCADE,
            path TEXT NOT NULL,
            kind TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            size INTEGER NOT NULL,
            content TEXT NULL,
            UNIQUE(library_version_id, path)
        );

        CREATE TABLE document_chunks (
            id TEXT PRIMARY KEY,
            library_version_id TEXT NOT NULL REFERENCES library_versions(id) ON DELETE CASCADE,
            artifact_id TEXT NULL REFERENCES artifacts(id) ON DELETE SET NULL,
            path TEXT NOT NULL,
            kind TEXT NOT NULL,
            member_name TEXT NULL,
            ordinal INTEGER NOT NULL,
            content TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            UNIQUE(library_version_id, path, member_name, ordinal)
        );

        CREATE VIRTUAL TABLE document_chunks_fts USING fts5(
            document_chunk_id UNINDEXED,
            package_id,
            version UNINDEXED,
            path,
            member_name,
            content,
            tokenize = 'unicode61'
        );

        CREATE TABLE symbols (
            id TEXT PRIMARY KEY,
            library_version_id TEXT NOT NULL REFERENCES library_versions(id) ON DELETE CASCADE,
            namespace TEXT NOT NULL,
            fully_qualified_name TEXT NOT NULL,
            kind TEXT NOT NULL,
            signature TEXT NOT NULL,
            containing_type TEXT NULL,
            assembly_path TEXT NOT NULL,
            target_framework TEXT NULL,
            xml_documentation_member TEXT NULL
        );

        CREATE INDEX ix_symbols_fully_qualified_name
            ON symbols(fully_qualified_name);

        CREATE TABLE dependencies (
            id TEXT PRIMARY KEY,
            library_version_id TEXT NOT NULL REFERENCES library_versions(id) ON DELETE CASCADE,
            package_id TEXT NOT NULL,
            version_range TEXT NOT NULL,
            target_framework TEXT NULL
        );

        CREATE TABLE target_frameworks (
            library_version_id TEXT NOT NULL REFERENCES library_versions(id) ON DELETE CASCADE,
            framework TEXT NOT NULL,
            PRIMARY KEY(library_version_id, framework)
        );

        CREATE TABLE index_runs (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL REFERENCES sources(id) ON DELETE CASCADE,
            status TEXT NOT NULL,
            started_at TEXT NOT NULL,
            completed_at TEXT NOT NULL,
            duration_ms INTEGER NOT NULL,
            indexed_count INTEGER NOT NULL,
            changed_count INTEGER NOT NULL,
            unchanged_count INTEGER NOT NULL,
            error_count INTEGER NOT NULL
        );

        CREATE TABLE index_run_errors (
            id TEXT PRIMARY KEY,
            index_run_id TEXT NOT NULL REFERENCES index_runs(id) ON DELETE CASCADE,
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            package_id TEXT NULL,
            version TEXT NULL
        );

        CREATE INDEX ix_libraries_normalized_package_id
            ON libraries(normalized_package_id);

        CREATE INDEX ix_library_versions_selection
            ON library_versions(library_id, is_listed, is_prerelease, version);

        CREATE INDEX ix_document_chunks_lookup
            ON document_chunks(library_version_id, kind, member_name);

        CREATE INDEX ix_symbols_lookup
            ON symbols(library_version_id, fully_qualified_name, target_framework);

        CREATE INDEX ix_symbols_containing_type
            ON symbols(library_version_id, containing_type);

        CREATE VIRTUAL TABLE libraries_fts USING fts5(
            library_id UNINDEXED,
            source_name UNINDEXED,
            package_id,
            title,
            description,
            summary,
            tags,
            document_text,
            tokenize = 'unicode61'
        );
        """;

    private const string MigrationV2Sql =
        """
        CREATE INDEX IF NOT EXISTS ix_libraries_normalized_package_id
            ON libraries(normalized_package_id);

        CREATE INDEX IF NOT EXISTS ix_library_versions_selection
            ON library_versions(library_id, is_listed, is_prerelease, version);

        CREATE INDEX IF NOT EXISTS ix_document_chunks_lookup
            ON document_chunks(library_version_id, kind, member_name);

        CREATE INDEX IF NOT EXISTS ix_symbols_lookup
            ON symbols(library_version_id, fully_qualified_name, target_framework);

        CREATE INDEX IF NOT EXISTS ix_symbols_containing_type
            ON symbols(library_version_id, containing_type);

        CREATE VIRTUAL TABLE IF NOT EXISTS libraries_fts USING fts5(
            library_id UNINDEXED,
            source_name UNINDEXED,
            package_id,
            title,
            description,
            summary,
            tags,
            document_text,
            tokenize = 'unicode61'
        );
        """;

    private const string MigrationV3Sql =
        """
        ALTER TABLE sources
            ADD COLUMN environment TEXT NOT NULL DEFAULT '';

        UPDATE sources
        SET environment = name
        WHERE environment = '';
        """;

    private const string MigrationV4Sql =
        """
        ALTER TABLE sources
            ADD COLUMN kind TEXT NOT NULL DEFAULT 'nuget';

        ALTER TABLE libraries
            ADD COLUMN kind TEXT NOT NULL DEFAULT 'nuget';

        ALTER TABLE libraries
            ADD COLUMN display_name TEXT NULL;

        UPDATE libraries
        SET display_name = package_id
        WHERE display_name IS NULL;

        ALTER TABLE artifacts
            ADD COLUMN content TEXT NULL;
        """;

    private static async Task RefreshAllLibrarySearchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM libraries_fts;",
            cancellationToken);
        await InsertLibrarySearchRowsAsync(
            connection,
            transaction,
            whereClause: string.Empty,
            parameters: [],
            cancellationToken);
    }

    private static async Task RefreshSourceLibrarySearchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            DELETE FROM libraries_fts
            WHERE library_id IN (SELECT id FROM libraries WHERE source_id = $sourceId);
            """,
            [("$sourceId", sourceId)],
            cancellationToken);
        await InsertLibrarySearchRowsAsync(
            connection,
            transaction,
            "WHERE l.source_id = $sourceId",
            [("$sourceId", sourceId)],
            cancellationToken);
    }

    private static async Task InsertLibrarySearchRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string whereClause,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            $$"""
            INSERT INTO libraries_fts (
                library_id, source_name, package_id, title, description, summary, tags, document_text)
            SELECT
                l.id,
                s.name,
                l.package_id,
                COALESCE(lv.title, ''),
                COALESCE(lv.description, ''),
                COALESCE(lv.summary, ''),
                COALESCE(lv.tags, ''),
                COALESCE((
                    SELECT substr(group_concat(dc.content, ' '), 1, 50000)
                    FROM document_chunks dc
                    WHERE dc.library_version_id = lv.id
                ), '')
            FROM libraries l
            INNER JOIN sources s ON s.id = l.source_id
            LEFT JOIN library_versions lv ON lv.id = (
                SELECT candidate.id
                FROM library_versions candidate
                WHERE candidate.library_id = l.id
                ORDER BY
                    candidate.is_listed DESC,
                    candidate.is_prerelease ASC,
                    COALESCE(candidate.published_at, candidate.indexed_at) DESC,
                    candidate.version DESC
                LIMIT 1
            )
            {{whereClause}};
            """,
            parameters,
            cancellationToken);
    }
}
