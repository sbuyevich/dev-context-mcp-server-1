using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Core.Services;
using DevContextMcp.IntegrationTests.Indexing;
using DevContextMcp.Server;
using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using DevContextMcp.Server.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.IntegrationTests.Retrieval;

public sealed class EnvironmentAwareRetrievalTests
{
    [Fact]
    public async Task SamePackageCanBeSelectedByEnvironmentAndVersion()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mcp-doc-environment-retrieval-{Guid.NewGuid():N}");
        var qa = Path.Combine(root, "qa");
        var production = Path.Combine(root, "production");
        var databasePath = Path.Combine(root, "index", "docs.db");

        FixtureNuGetPackage.Create(
            qa,
            "2.0.0",
            "# QA\n\nQA version 2.0.0.");
        FixtureNuGetPackage.Create(
            qa,
            "2.1.0",
            "# QA\n\nQA recommended version 2.1.0.");
        FixtureNuGetPackage.Create(
            production,
            "1.0.0",
            "# Production\n\nProduction version 1.0.0.");

        try
        {
            using var provider = CreateProvider(qa, production, databasePath);
            var summaries = await provider.GetRequiredService<IIndexCoordinator>()
                .IndexAllAsync(CancellationToken.None);
            Assert.Equal(2, summaries.Count);
            Assert.All(summaries, summary => Assert.Equal("succeeded", summary.Status));

            var resolver = provider.GetRequiredService<IResolveLibraryHandler>();
            var all = await resolver.HandleAsync(
                new ResolveLibraryRequest(FixtureNuGetPackage.PackageId),
                CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, all.Status);
            Assert.Equal(2, all.Data!.Matches.Count);

            var qaMatch = Assert.Single(all.Data.Matches, match =>
                match.Environment.Equals("qa", StringComparison.OrdinalIgnoreCase));
            Assert.Equal($"nuget:qa/{FixtureNuGetPackage.PackageId}", qaMatch.LibraryId);
            Assert.Equal("qa", qaMatch.SourceId);
            Assert.Equal("2.1.0", qaMatch.RecommendedVersion);

            var productionMatch = Assert.Single(all.Data.Matches, match =>
                match.Environment.Equals("production", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("production", productionMatch.SourceId);
            Assert.Equal("1.0.0", productionMatch.RecommendedVersion);

            var filtered = await resolver.HandleAsync(
                new ResolveLibraryRequest(
                    FixtureNuGetPackage.PackageId,
                    Environment: "QA"),
                CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, filtered.Status);
            Assert.Equal("qa", Assert.Single(filtered.Data!.Matches).Environment);

            var versionsHandler = provider.GetRequiredService<IListVersionsHandler>();
            var legacy = await versionsHandler.HandleAsync(
                new ListVersionsRequest($"nuget:{FixtureNuGetPackage.PackageId}"),
                CancellationToken.None);
            Assert.Equal("production", legacy.ResolvedContext!.Environment);
            Assert.Equal("production", legacy.ResolvedContext.SourceId);

            var qaVersions = await versionsHandler.HandleAsync(
                new ListVersionsRequest($"nuget:qa/{FixtureNuGetPackage.PackageId}"),
                CancellationToken.None);
            Assert.Equal("qa", qaVersions.ResolvedContext!.Environment);
            Assert.Equal("qa", qaVersions.ResolvedContext.SourceId);
            Assert.Equal("2.1.0", qaVersions.Data!.RecommendedVersion);

            var docsHandler = provider.GetRequiredService<IQueryDocsHandler>();
            var qaDocs = await docsHandler.HandleAsync(
                new QueryDocsRequest(
                    $"nuget:qa/{FixtureNuGetPackage.PackageId}",
                    "QA",
                    Version: "2.0.0"),
                CancellationToken.None);
            Assert.Equal(ToolResultStatus.Ok, qaDocs.Status);
            Assert.Equal("qa", qaDocs.ResolvedContext!.SourceId);
            Assert.Contains(qaDocs.Evidence, item =>
                item.Text.Contains("QA version", StringComparison.Ordinal));

            var isolated = await docsHandler.HandleAsync(
                new QueryDocsRequest(
                    $"nuget:qa/{FixtureNuGetPackage.PackageId}",
                    "Production",
                    Version: "1.0.0"),
                CancellationToken.None);
            Assert.Equal(ToolResultStatus.NotFound, isolated.Status);
            Assert.Equal("version_not_found", Assert.Single(isolated.Errors).Code);

            var missingEnvironment = await versionsHandler.HandleAsync(
                new ListVersionsRequest(
                    $"nuget:staging/{FixtureNuGetPackage.PackageId}"),
                CancellationToken.None);
            Assert.Equal(ToolResultStatus.NotFound, missingEnvironment.Status);
            Assert.Equal(
                "environment_not_found",
                Assert.Single(missingEnvironment.Errors).Code);
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
        string qa,
        string production,
        string databasePath)
    {
        var values = new Dictionary<string, string?>
        {
            ["DevContextMcp:DatabasePath"] = databasePath,
            ["DevContextMcp:Retrieval:EnvironmentOrder:0"] = "production",
            ["DevContextMcp:Retrieval:EnvironmentOrder:1"] = "qa",
            [$"DevContextMcp:RecommendedVersions:{FixtureNuGetPackage.PackageId}"] = "1.0.0",
            [$"DevContextMcp:RecommendedVersions:nuget:qa/{FixtureNuGetPackage.PackageId}"] = "2.1.0",
            ["DevContextMcp:Indexing:MaxCompressionRatio"] = "10000"
        };
        var root = Directory.GetParent(qa)!.FullName;
        values["DevContextMcp:NuGetSourcesPath"] =
            FixtureNuGetConfiguration.CreatePackageFolder(
                root,
                new FixtureNuGetConfiguration.PackagePolicy(
                    "qa",
                    FixtureNuGetPackage.PackageId),
                new FixtureNuGetConfiguration.PackagePolicy(
                    "production",
                    FixtureNuGetPackage.PackageId));
        AddSource(values, 0, "qa", qa);
        AddSource(values, 1, "production", production);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDevContextMcpCore(configuration);
        services.AddIndexerCli(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void AddSource(
        IDictionary<string, string?> values,
        int index,
        string name,
        string serviceIndex)
    {
        var prefix = $"DevContextMcp:Environments:{index}";
        values[$"{prefix}:Name"] = name;
        values[$"{prefix}:ServiceIndex"] = serviceIndex;
        values[$"{prefix}:MaxPackages"] = "10";
    }
}
