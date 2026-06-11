using DevContextMcp.Server;
using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.IntegrationTests.Startup;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public async Task InMemoryServerStartsAndStopsCleanly()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = await Mcp.McpTestServer.StartAsync(timeout.Token);

        var tools = await server.Client.ListToolsAsync(cancellationToken: timeout.Token);

        Assert.Equal(4, tools.Count);
    }

    [Fact]
    public void HostCoreDoesNotRegisterIndexingServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = "data/docs.db"
                })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDevContextMcpCore(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IIndexCoordinator>());
        Assert.Null(provider.GetService<IIndexStore>());
        Assert.Null(provider.GetService<IPackageSourceClient>());
        Assert.Null(provider.GetService<IPackageProcessor>());
    }
}
