using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;
using DevContextMcp.Server.Core.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace DevContextMcp.Infrastructure.Server;

internal sealed class SqliteNuGetReadStore : INuGetReadStore
{
    private const int RequiredSchemaVersion = 3;

    public async Task<IReadOnlyList<LibraryCandidateRecord>> SearchLibrariesAsync(
        string databasePath,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        var candidates = new Dictionary<string, LibraryCandidateRecord>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT
                    l.id,
                    s.name,
                    s.environment,
                    l.package_id,
                    lv.description,
                    lv.version,
                    COALESCE(lv.is_listed, 0),
                    COALESCE(lv.is_prerelease, 0),
                    COALESCE(lv.is_deprecated, 0)
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
                WHERE l.normalized_package_id = lower($query)
                   OR l.normalized_package_id LIKE lower($query) || '%'
                ORDER BY l.normalized_package_id, s.name
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", query.Trim());
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var packageId = reader.GetString(3);
                candidates[reader.GetString(0)] = new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    packageId,
                    GetNullableString(reader, 4),
                    GetNullableString(reader, 5),
                    reader.GetInt64(6) != 0,
                    reader.GetInt64(7) != 0,
                    reader.GetInt64(8) != 0,
                    packageId.Equals(query, StringComparison.OrdinalIgnoreCase),
                    packageId.StartsWith(query, StringComparison.OrdinalIgnoreCase),
                    0);
            }
        }

        var ftsQuery = FtsQueryBuilder.Build(query);
        if (ftsQuery.Length > 0)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    l.id,
                    s.name,
                    s.environment,
                    l.package_id,
                    lv.description,
                    lv.version,
                    COALESCE(lv.is_listed, 0),
                    COALESCE(lv.is_prerelease, 0),
                    COALESCE(lv.is_deprecated, 0),
                    bm25(libraries_fts)
                FROM libraries_fts
                INNER JOIN libraries l ON l.id = libraries_fts.library_id
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
                WHERE libraries_fts MATCH $query
                ORDER BY bm25(libraries_fts), l.normalized_package_id, s.name
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$query", ftsQuery);
            command.Parameters.AddWithValue("$limit", limit);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var position = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var libraryId = reader.GetString(0);
                var packageId = reader.GetString(3);
                var textScore = Math.Max(0.35, 0.75 - (position++ * 0.02));
                var record = new LibraryCandidateRecord(
                    libraryId,
                    reader.GetString(1),
                    reader.GetString(2),
                    packageId,
                    GetNullableString(reader, 4),
                    GetNullableString(reader, 5),
                    reader.GetInt64(6) != 0,
                    reader.GetInt64(7) != 0,
                    reader.GetInt64(8) != 0,
                    packageId.Equals(query, StringComparison.OrdinalIgnoreCase),
                    packageId.StartsWith(query, StringComparison.OrdinalIgnoreCase),
                    textScore);

                if (!candidates.TryGetValue(libraryId, out var existing)
                    || record.TextScore > existing.TextScore)
                {
                    candidates[libraryId] = record;
                }
            }
        }

        return candidates.Values.ToArray();
    }

    public async Task<IReadOnlyList<ResolvedLibraryRecord>> FindLibrariesAsync(
        string databasePath,
        string packageId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.id, s.name, s.environment, l.package_id, lv.description
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
            WHERE l.normalized_package_id = lower($packageId)
            ORDER BY s.name;
            """;
        command.Parameters.AddWithValue("$packageId", packageId);

        var values = new List<ResolvedLibraryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                GetNullableString(reader, 4)));
        }

        return values;
    }

    public async Task<bool> EnvironmentExistsAsync(
        string databasePath,
        string environment,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sources
                WHERE environment = $environment COLLATE NOCASE
            );
            """;
        command.Parameters.AddWithValue("$environment", environment);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) != 0;
    }

    public async Task<IReadOnlyList<IndexedVersionRecord>> ListVersionsAsync(
        string databasePath,
        string libraryId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, version, is_listed, is_prerelease, is_deprecated, published_at
            FROM library_versions
            WHERE library_id = $libraryId
            ORDER BY version;
            """;
        command.Parameters.AddWithValue("$libraryId", libraryId);

        var versions = new List<IndexedVersionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetInt64(3) != 0,
                reader.GetInt64(4) != 0,
                ParseDate(GetNullableString(reader, 5))));
        }

        return versions;
    }

    public async Task<IReadOnlyList<DocumentHitRecord>> SearchDocumentsAsync(
        string databasePath,
        string libraryVersionId,
        string question,
        int limit,
        CancellationToken cancellationToken)
    {
        var ftsQuery = FtsQueryBuilder.Build(question);
        if (ftsQuery.Length == 0)
        {
            return [];
        }

        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                dc.path,
                dc.kind,
                dc.member_name,
                dc.content,
                dc.content_hash,
                bm25(document_chunks_fts)
            FROM document_chunks_fts
            INNER JOIN document_chunks dc
                ON dc.id = document_chunks_fts.document_chunk_id
            WHERE document_chunks_fts MATCH $query
              AND dc.library_version_id = $libraryVersionId
            ORDER BY bm25(document_chunks_fts), dc.path, dc.ordinal
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", ftsQuery);
        command.Parameters.AddWithValue("$libraryVersionId", libraryVersionId);
        command.Parameters.AddWithValue("$limit", limit);

        var hits = new List<DocumentHitRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var position = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                GetNullableString(reader, 2),
                reader.GetString(3),
                reader.GetString(4),
                Math.Max(0.2, 0.7 - (position++ * 0.02))));
        }

        return hits;
    }

    public async Task<IReadOnlyList<SymbolHitRecord>> SearchSymbolsAsync(
        string databasePath,
        string libraryVersionId,
        string query,
        string? targetFramework,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                s.fully_qualified_name,
                s.kind,
                s.signature,
                s.containing_type,
                s.assembly_path,
                s.target_framework,
                s.xml_documentation_member,
                dc.content
            FROM symbols s
            LEFT JOIN document_chunks dc
                ON dc.library_version_id = s.library_version_id
               AND dc.member_name = s.xml_documentation_member
            WHERE s.library_version_id = $libraryVersionId
              AND lower(s.fully_qualified_name) LIKE '%' || lower($query) || '%'
              AND ($targetFramework IS NULL
                   OR s.target_framework IS NULL
                   OR lower(s.target_framework) = lower($targetFramework))
            ORDER BY s.fully_qualified_name, s.signature
            LIMIT 200;
            """;
        command.Parameters.AddWithValue("$libraryVersionId", libraryVersionId);
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$targetFramework", targetFramework ?? (object)DBNull.Value);

        var hits = new List<SymbolHitRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fullyQualifiedName = reader.GetString(0);
            hits.Add(new(
                fullyQualifiedName,
                reader.GetString(1),
                reader.GetString(2),
                GetNullableString(reader, 3),
                reader.GetString(4),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                GetNullableString(reader, 7),
                GetMatchTier(fullyQualifiedName, query)));
        }

        return hits
            .OrderBy(hit => hit.MatchTier)
            .ThenBy(hit => hit.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(hit => hit.Signature, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<SymbolHitRecord>> GetRelatedSymbolsAsync(
        string databasePath,
        string libraryVersionId,
        string containingType,
        string fullyQualifiedName,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                fully_qualified_name,
                kind,
                signature,
                containing_type,
                assembly_path,
                target_framework,
                xml_documentation_member
            FROM symbols
            WHERE library_version_id = $libraryVersionId
              AND containing_type = $containingType
              AND fully_qualified_name <> $fullyQualifiedName
            ORDER BY fully_qualified_name, signature
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$libraryVersionId", libraryVersionId);
        command.Parameters.AddWithValue("$containingType", containingType);
        command.Parameters.AddWithValue("$fullyQualifiedName", fullyQualifiedName);
        command.Parameters.AddWithValue("$limit", limit);

        var hits = new List<SymbolHitRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                GetNullableString(reader, 3),
                reader.GetString(4),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                null,
                0));
        }

        return hits;
    }

    public Task<ResourceDocumentRecord?> ReadArtifactAsync(
        string databasePath,
        string sourceName,
        string packageId,
        string version,
        string path,
        CancellationToken cancellationToken) =>
        ReadResourceAsync(
            databasePath,
            """
            SELECT dc.content, dc.kind
            FROM document_chunks dc
            INNER JOIN library_versions lv ON lv.id = dc.library_version_id
            INNER JOIN libraries l ON l.id = lv.library_id
            INNER JOIN sources s ON s.id = l.source_id
            WHERE s.name = $sourceName
              AND lower(l.package_id) = lower($packageId)
              AND lv.version = $version
              AND dc.path = $path
            ORDER BY dc.ordinal;
            """,
            [
                ("$sourceName", sourceName),
                ("$packageId", packageId),
                ("$version", version),
                ("$path", path)
            ],
            isSymbol: false,
            cancellationToken);

    public Task<ResourceDocumentRecord?> ReadSymbolAsync(
        string databasePath,
        string sourceName,
        string packageId,
        string version,
        string qualifiedName,
        CancellationToken cancellationToken) =>
        ReadResourceAsync(
            databasePath,
            """
            SELECT s.signature, COALESCE(dc.content, '')
            FROM symbols s
            INNER JOIN library_versions lv ON lv.id = s.library_version_id
            INNER JOIN libraries l ON l.id = lv.library_id
            INNER JOIN sources src ON src.id = l.source_id
            LEFT JOIN document_chunks dc
                ON dc.library_version_id = s.library_version_id
               AND dc.member_name = s.xml_documentation_member
            WHERE src.name = $sourceName
              AND lower(l.package_id) = lower($packageId)
              AND lv.version = $version
              AND s.fully_qualified_name = $qualifiedName
            ORDER BY s.signature
            LIMIT 1;
            """,
            [
                ("$sourceName", sourceName),
                ("$packageId", packageId),
                ("$version", version),
                ("$qualifiedName", qualifiedName)
            ],
            isSymbol: true,
            cancellationToken);

    private static async Task<ResourceDocumentRecord?> ReadResourceAsync(
        string databasePath,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        bool isSymbol,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var parts = new List<string>();
        string? kind = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (isSymbol)
            {
                parts.Add(reader.GetString(0));
                var documentation = reader.GetString(1);
                if (documentation.Length > 0)
                {
                    parts.Add(documentation);
                }
            }
            else
            {
                parts.Add(reader.GetString(0));
                kind ??= reader.GetString(1);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var mimeType = isSymbol
            ? "text/plain"
            : kind == "readme" || kind == "text_documentation"
                ? "text/markdown"
                : "application/xml";
        return new(string.Join(Environment.NewLine + Environment.NewLine, parts), mimeType);
    }

    private static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
        {
            throw new IndexUnavailableException($"The documentation index does not exist at '{path}'.");
        }

        try
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (version < RequiredSchemaVersion)
            {
                await connection.DisposeAsync();
                throw new IndexUnavailableException(
                    $"The documentation index schema is {version}; version {RequiredSchemaVersion} is required.");
            }

            return connection;
        }
        catch (IndexUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            throw new IndexUnavailableException("The documentation index could not be opened.", exception);
        }
    }

    private static int GetMatchTier(string fullyQualifiedName, string query)
    {
        if (fullyQualifiedName.Equals(query, StringComparison.Ordinal))
        {
            return 0;
        }

        if (fullyQualifiedName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var simpleName = fullyQualifiedName.Split('.').Last();
        if (simpleName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (fullyQualifiedName.EndsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
