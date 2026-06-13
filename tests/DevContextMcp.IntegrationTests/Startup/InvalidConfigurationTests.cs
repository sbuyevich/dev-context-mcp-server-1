using DevContextMcp.Server;
using DevContextMcp.Indexer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevContextMcp.IntegrationTests.Startup;

public sealed class InvalidConfigurationTests
{
    [Fact]
    public async Task InvalidConfigurationFailsStartup()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = [],
                DisableDefaults = true
            });

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DevContextMcp:DatabasePath"] = "data/docs.db",
                ["DevContextMcp:Retrieval:DefaultMaxResults"] = "0"
            });
        builder.Logging.ClearProviders();
        builder.Services.AddDevContextMcpCore(builder.Configuration);

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(CancellationToken.None));

        Assert.Contains("DefaultMaxResults", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidIndexerConfigurationFailsStartup()
    {
        var sourcesPath = Path.Combine(
            Path.GetTempPath(),
            $"mcp-doc-invalid-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourcesPath);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = [],
                DisableDefaults = true
            });

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DevContextMcp:DatabasePath"] = "data/docs.db",
                ["DevContextMcp:NugetsPath"] = sourcesPath,
                ["DevContextMcp:Indexing:MaxPackageBytes"] = "0"
            });
        builder.Logging.ClearProviders();
        builder.Services.AddIndexerCli(builder.Configuration);

        try
        {
            using var host = builder.Build();

            var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
                host.StartAsync(CancellationToken.None));

            Assert.Contains("MaxPackageBytes", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sourcesPath, recursive: true);
        }
    }
}
