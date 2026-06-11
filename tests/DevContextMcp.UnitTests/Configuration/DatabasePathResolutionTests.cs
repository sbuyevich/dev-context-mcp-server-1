using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Server;
using DevContextMcp.Server.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RetrievalConfigurationProvider = DevContextMcp.Server.Core.Services.IConfigurationProvider;

namespace DevContextMcp.UnitTests.Configuration;

public sealed class DatabasePathResolutionTests
{
    [Fact]
    public void ServerResolvesRelativeDatabasePathFromExecutableDirectory()
    {
        var relativePath = Path.Combine("data", "server.db");
        using var provider = CreateServerProvider(relativePath);

        var settings = provider
            .GetRequiredService<RetrievalConfigurationProvider>()
            .GetSettings();

        Assert.Equal(
            Path.GetFullPath(relativePath, AppContext.BaseDirectory),
            settings.DatabasePath);
    }

    [Fact]
    public void ServerPreservesAbsoluteDatabasePath()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "server.db");
        using var provider = CreateServerProvider(absolutePath);

        var settings = provider
            .GetRequiredService<RetrievalConfigurationProvider>()
            .GetSettings();

        Assert.Equal(Path.GetFullPath(absolutePath), settings.DatabasePath);
    }

    [Fact]
    public void IndexerResolvesRelativeDatabasePathFromExecutableDirectory()
    {
        var relativePath = Path.Combine("data", "indexer.db");
        var settings = CreateIndexerSettings(relativePath);

        Assert.Equal(
            Path.GetFullPath(relativePath, AppContext.BaseDirectory),
            settings.DatabasePath);
    }

    [Fact]
    public void IndexerPreservesAbsoluteDatabasePath()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), "indexer.db");
        var settings = CreateIndexerSettings(absolutePath);

        Assert.Equal(Path.GetFullPath(absolutePath), settings.DatabasePath);
    }

    private static ServiceProvider CreateServerProvider(string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = databasePath
                })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDevContextMcpCore(configuration);

        return services.BuildServiceProvider();
    }

    private static DevContextMcp.Indexer.Core.Models.IndexingSettings
        CreateIndexerSettings(string databasePath)
    {
        using var folder = PackageFolder.Create();
        var options = Options.Create(
            new IndexerOptions
            {
                DatabasePath = databasePath,
                NuGetSourcesPath = folder.Path
            });

        return new OptionsIndexingConfigurationProvider(
                options,
                new NuGetPackageOptionsLoader())
            .GetSettings();
    }

    private sealed class PackageFolder : IDisposable
    {
        private PackageFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static PackageFolder Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"nuget-options-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
