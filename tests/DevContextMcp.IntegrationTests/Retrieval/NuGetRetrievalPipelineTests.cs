using System.Text.Json;
using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using DevContextMcp.Server.Core.Retrieval.Services;
using DevContextMcp.Server;
using DevContextMcp.Indexer.Cli;
using DevContextMcp.Indexer.Services;
using DevContextMcp.IntegrationTests.Indexing;
using DevContextMcp.IntegrationTests.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DevContextMcp.IntegrationTests.Retrieval;

public sealed class NuGetRetrievalPipelineTests
{
    [Fact]
    public async Task IndexedPackagesAreRetrievableThroughHandlersAndMcp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcp-doc-retrieval-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var databasePath = Path.Combine(root, "index", "docs.db");
        FixtureNuGetPackage.Create(feed, "1.2.3");
        FixtureNuGetPackage.Create(
            feed,
            "2.0.0",
            "# Fixture Documentation\n\nVersion 2.0.0 contains a distinct retrieval marker.");
        FixtureNuGetPackage.Create(
            feed,
            "3.0.0-beta.1",
            "# Fixture Documentation\n\nPrerelease version content.");

        try
        {
            using var provider = CreateProvider(feed, databasePath);
            var coordinator = provider.GetRequiredService<IIndexCoordinator>();
            var indexed = Assert.Single(await coordinator.IndexAllAsync(CancellationToken.None));
            Assert.Equal("succeeded", indexed.Status);
            Assert.Equal(3, indexed.Indexed);

            var exact = await provider.GetRequiredService<IResolveLibraryHandler>()
                .HandleAsync(
                    new ResolveLibraryRequest(FixtureNuGetPackage.PackageId),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, exact.Status);
            Assert.Equal(
                $"nuget:test/{FixtureNuGetPackage.PackageId}",
                Assert.Single(exact.Data!.Matches).LibraryId);

            var descriptive = await provider.GetRequiredService<IResolveLibraryHandler>()
                .HandleAsync(
                    new ResolveLibraryRequest("deterministic documentation"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, descriptive.Status);

            var versions = await provider.GetRequiredService<IListVersionsHandler>()
                .HandleAsync(
                    new ListVersionsRequest($"nuget:{FixtureNuGetPackage.PackageId}"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, versions.Status);
            Assert.Equal("test", versions.ResolvedContext!.Environment);
            Assert.Equal("fixture", versions.ResolvedContext.SourceId);
            Assert.Equal(["2.0.0", "1.2.3"], versions.Data!.Versions.Select(item => item.Version));
            Assert.Equal("1.2.3", versions.Data.RecommendedVersion);
            Assert.Equal("configured_recommendation", versions.Data.RecommendedVersionReason);

            var docs = await provider.GetRequiredService<IQueryDocsHandler>()
                .HandleAsync(
                    new QueryDocsRequest(
                        $"nuget:{FixtureNuGetPackage.PackageId}",
                        "indexed package behavior",
                        Version: "1.2.3"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, docs.Status);
            Assert.Equal("1.2.3", docs.ResolvedContext!.Version);
            Assert.Contains(docs.Evidence, item =>
                item.Text.Contains("Version 1.2.3", StringComparison.Ordinal));
            Assert.DoesNotContain(docs.Evidence, item =>
                item.Text.Contains("Version 2.0.0", StringComparison.Ordinal));
            Assert.All(docs.Evidence, item => Assert.StartsWith("nuget://", item.CitationUri));

            var symbol = await provider.GetRequiredService<IGetSymbolHandler>()
                .HandleAsync(
                    new GetSymbolRequest(
                        $"nuget:{FixtureNuGetPackage.PackageId}",
                        "DevContextMcp.Indexer.Models.PackageIndexData",
                        Version: "1.2.3"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, symbol.Status);
            Assert.Contains("PackageIndexData", symbol.Data!.Symbol!.Signature, StringComparison.Ordinal);
            Assert.Contains("Fixture XML documentation", symbol.Data.Symbol.Documentation, StringComparison.Ordinal);

            var missing = await provider.GetRequiredService<IGetSymbolHandler>()
                .HandleAsync(
                    new GetSymbolRequest(
                        $"nuget:{FixtureNuGetPackage.PackageId}",
                        "Definitely.Missing",
                        Version: "1.2.3"),
                    CancellationToken.None);
            Assert.Equal(ToolResultStatus.NotFound, missing.Status);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var server = await McpTestServer.StartAsync(
                timeout.Token,
                new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = databasePath,
                    [$"DevContextMcp:RecommendedVersions:{FixtureNuGetPackage.PackageId}"] = "1.2.3"
                });

            var toolResult = await server.Client.CallToolAsync(
                "resolve_library",
                new Dictionary<string, object?>
                {
                    ["query"] = FixtureNuGetPackage.PackageId
                },
                cancellationToken: timeout.Token);
            var toolResponse = toolResult.StructuredContent!.Value
                .Deserialize<ResolveLibraryResponse>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(ToolResultStatus.Ok, toolResponse!.Status);

            var wrappedToolResult = await server.Client.CallToolAsync(
                "resolve_library",
                new Dictionary<string, object?>
                {
                    ["query"] = JsonSerializer.Serialize(new
                    {
                        query = FixtureNuGetPackage.PackageId,
                        includePrerelease = false,
                        limit = 10,
                        environment = "test"
                    })
                },
                cancellationToken: timeout.Token);
            var wrappedToolResponse = wrappedToolResult.StructuredContent!.Value
                .Deserialize<ResolveLibraryResponse>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(ToolResultStatus.Ok, wrappedToolResponse!.Status);
            Assert.Equal(
                FixtureNuGetPackage.PackageId,
                Assert.Single(wrappedToolResponse.Data!.Matches).DisplayName);
            Assert.Equal(
                "test",
                Assert.Single(wrappedToolResponse.Data.Matches).Environment);

            var templates = await server.Client.ListResourceTemplatesAsync(
                cancellationToken: timeout.Token);
            Assert.Contains(templates, template =>
                template.UriTemplate.Contains("/artifact/", StringComparison.Ordinal));
            Assert.Contains(templates, template =>
                template.UriTemplate.Contains("/symbol/", StringComparison.Ordinal));

            var resource = await server.Client.ReadResourceAsync(
                "nuget://fixture/Fixture.Documentation/1.2.3/artifact/README.md",
                cancellationToken: timeout.Token);
            var text = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
            Assert.Contains("Version 1.2.3", text.Text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ServiceProvider CreateProvider(string feed, string databasePath)
    {
        var root = Directory.GetParent(feed)!.FullName;
        var sourcesPath = FixtureNuGetConfiguration.CreatePackageFolder(
            root,
            new FixtureNuGetConfiguration.PackagePolicy(
                "test",
                FixtureNuGetPackage.PackageId,
                IncludePrerelease: true));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevContextMcp:DatabasePath"] = databasePath,
                ["DevContextMcp:NuGetSourcesPath"] = sourcesPath,
                ["DevContextMcp:Environments:0:Name"] = "fixture",
                ["DevContextMcp:Environments:0:Environment"] = "test",
                ["DevContextMcp:Environments:0:ServiceIndex"] = feed,
                ["DevContextMcp:Environments:0:MaxPackages"] = "10",
                ["DevContextMcp:Indexing:MaxCompressionRatio"] = "10000",
                [$"DevContextMcp:RecommendedVersions:{FixtureNuGetPackage.PackageId}"] = "1.2.3"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDevContextMcpCore(configuration);
        services.AddIndexerCli(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
