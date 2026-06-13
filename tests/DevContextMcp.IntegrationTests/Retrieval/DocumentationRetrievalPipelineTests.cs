using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
using DevContextMcp.IntegrationTests.Mcp;
using DevContextMcp.Server;
using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DevContextMcp.IntegrationTests.Retrieval;

public sealed class DocumentationRetrievalPipelineTests
{
    [Fact]
    public async Task CompanyDocsAreIndexedReplacedAndExposedAsVersionlessLibrary()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mcp-company-docs-{Guid.NewGuid():N}");
        var docsRoot = Path.Combine(root, "company-docs");
        var databasePath = Path.Combine(root, "index", "docs.db");
        Directory.CreateDirectory(Path.Combine(docsRoot, "testing"));
        Directory.CreateDirectory(Path.Combine(docsRoot, ".hidden"));
        await File.WriteAllTextAsync(
            Path.Combine(docsRoot, "testing", "standards.md"),
            "# Unit testing standards\n\nUse deterministic fixtures and assert behavior.");
        await File.WriteAllTextAsync(
            Path.Combine(docsRoot, "review.txt"),
            "Code reviews must identify regression risk.");
        await File.WriteAllTextAsync(
            Path.Combine(docsRoot, "ignored.json"),
            """{ "ignored": true }""");
        await File.WriteAllTextAsync(
            Path.Combine(docsRoot, ".hidden", "secret.md"),
            "This hidden document must not be indexed.");

        try
        {
            using var provider = CreateProvider(docsRoot, databasePath);
            var coordinator = provider.GetRequiredService<IIndexCoordinator>();

            var firstResult = await coordinator.IndexAllAsync(CancellationToken.None);
            var first = Assert.Single(firstResult.Summaries);
            Assert.Equal("succeeded", first.Status);
            Assert.Equal(2, first.Discovered);
            Assert.Equal(1, first.Changed);
            Assert.Equal(
                ["review.txt", "testing/standards.md"],
                firstResult.IndexedDocuments);
            Assert.Equal(
                [new PackageIdentityKey("company-docs", "current")],
                first.Added);

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(4L, await ScalarAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(
                2L,
                await ScalarAsync(
                    connection,
                    "SELECT COUNT(*) FROM artifacts WHERE kind = 'company_document';"));
            Assert.Equal(
                "docs",
                await TextScalarAsync(connection, "SELECT kind FROM libraries;"));

            var unchangedResult = await coordinator.IndexAllAsync(
                CancellationToken.None);
            var unchanged = Assert.Single(unchangedResult.Summaries);
            Assert.Equal(0, unchanged.Changed);
            Assert.Equal(1, unchanged.Unchanged);
            Assert.Equal(
                ["review.txt", "testing/standards.md"],
                unchangedResult.IndexedDocuments);

            var resolved = await provider.GetRequiredService<IResolveLibraryHandler>()
                .HandleAsync(
                    new ResolveLibraryRequest("unit testing standards"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, resolved.Status);
            var match = Assert.Single(resolved.Data!.Matches);
            Assert.Equal("docs:company-docs", match.LibraryId);
            Assert.Equal("docs", match.Kind);
            Assert.Equal("Company Docs", match.DisplayName);
            Assert.Null(match.Environment);
            Assert.Null(match.RecommendedVersion);

            var query = await provider.GetRequiredService<IQueryDocsHandler>()
                .HandleAsync(
                    new QueryDocsRequest(
                        "docs:company-docs",
                        "deterministic fixtures",
                        Version: "9.9.9",
                        TargetFramework: "net10.0",
                        IncludePrerelease: true),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, query.Status);
            Assert.Null(query.ResolvedContext!.Version);
            Assert.Contains(query.Warnings, warning =>
                warning.Code == "parameter_not_applicable");
            Assert.Contains(query.Evidence, evidence =>
                evidence.Text.Contains("deterministic fixtures", StringComparison.Ordinal));
            Assert.All(query.Citations, citation =>
                Assert.StartsWith("docs://company-docs/", citation.Uri, StringComparison.Ordinal));

            var versions = await provider.GetRequiredService<IListVersionsHandler>()
                .HandleAsync(
                    new ListVersionsRequest("docs:company-docs"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, versions.Status);
            Assert.Empty(versions.Data!.Versions);
            Assert.Contains(versions.Warnings, warning =>
                warning.Code == "version_not_applicable");

            var symbol = await provider.GetRequiredService<IGetSymbolHandler>()
                .HandleAsync(
                    new GetSymbolRequest("docs:company-docs", "Example.Type"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.NotFound, symbol.Status);
            Assert.Contains(symbol.Errors, error =>
                error.Code == "symbol_lookup_not_supported");

            File.Delete(Path.Combine(docsRoot, "review.txt"));
            await File.WriteAllTextAsync(
                Path.Combine(docsRoot, "testing", "standards.md"),
                "# Unit testing standards\n\nPrefer behavior-focused tests.");
            await File.WriteAllTextAsync(
                Path.Combine(docsRoot, "security.md"),
                "# Security standards\n\nNever log credentials.");
            var updated = Assert.Single(
                (await coordinator.IndexAllAsync(CancellationToken.None)).Summaries);
            Assert.Single(updated.Updated);
            Assert.Equal(
                0L,
                await ScalarAsync(
                    connection,
                    "SELECT COUNT(*) FROM artifacts WHERE path = 'review.txt';"));
            Assert.Equal(
                1L,
                await ScalarAsync(
                    connection,
                    "SELECT COUNT(*) FROM artifacts WHERE path = 'security.md';"));

            await File.WriteAllBytesAsync(
                Path.Combine(docsRoot, "broken.md"),
                [0xC3, 0x28]);
            var failed = Assert.Single(
                (await coordinator.IndexAllAsync(CancellationToken.None)).Summaries);
            Assert.Equal("failed", failed.Status);
            Assert.Single(failed.Errors);
            var stored = await provider.GetRequiredService<INuGetReadStore>()
                .ReadDocumentationAsync(
                    databasePath,
                    "security.md",
                    CancellationToken.None);
            Assert.Contains("Never log credentials", stored!.Text, StringComparison.Ordinal);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var server = await McpTestServer.StartAsync(
                timeout.Token,
                new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = databasePath
                });
            var templates = await server.Client.ListResourceTemplatesAsync(
                cancellationToken: timeout.Token);
            Assert.Contains(templates, template =>
                template.UriTemplate == "docs://company-docs/{path}");

            var resource = await server.Client.ReadResourceAsync(
                "docs://company-docs/security.md",
                cancellationToken: timeout.Token);
            var text = Assert.IsType<TextResourceContents>(
                Assert.Single(resource.Contents));
            Assert.Contains("Never log credentials", text.Text, StringComparison.Ordinal);
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
        string docsRoot,
        string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevContextMcp:DatabasePath"] = databasePath,
                ["DevContextMcp:Documentation:RootPath"] = docsRoot,
                ["DevContextMcp:Documentation:Extensions:0"] = ".md",
                ["DevContextMcp:Documentation:Extensions:1"] = ".txt"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDevContextMcpCore(configuration);
        services.AddIndexerCli(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<long> ScalarAsync(
        SqliteConnection connection,
        string sql)
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
