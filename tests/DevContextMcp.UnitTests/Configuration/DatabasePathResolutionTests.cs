using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Server;
using DevContextMcp.Server.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevContextMcp.UnitTests.Configuration;

public sealed class ServerDatabasePathResolutionTests : IDisposable
{
    private readonly IConfigurationRoot _configuration;
    private readonly ServiceProvider _serviceProvider;

    public ServerDatabasePathResolutionTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DevContextMcp:DatabasePath"] = "default.db"
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDevContextMcpCore(_configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    // Purpose: resolves a relative server database path from the executable directory
    [Fact]
    public void GetSettings_RelativeServerDatabasePath_ResolvesFromExecutableDirectory()
    {
        // arrange
        var relativePath = Path.Combine("data", "server.db");
        _configuration["DevContextMcp:DatabasePath"] = relativePath;

        // act
        var actual = _serviceProvider.GetRequiredService<RetrievalSettings>();

        // assert
        Assert.Equal(
            Path.GetFullPath(relativePath, AppContext.BaseDirectory),
            actual.DatabasePath);
    }

    // Purpose: preserves an absolute server database path
    [Fact]
    public void GetSettings_AbsoluteServerDatabasePath_PreservesAbsolutePath()
    {
        // arrange
        var absolutePath = Path.Combine(Path.GetTempPath(), "server.db");
        _configuration["DevContextMcp:DatabasePath"] = absolutePath;

        // act
        var actual = _serviceProvider.GetRequiredService<RetrievalSettings>();

        // assert
        Assert.Equal(Path.GetFullPath(absolutePath), actual.DatabasePath);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}

public sealed class IndexerDatabasePathResolutionTests
{
    private readonly IndexerOptions _options;
    private readonly OptionsIndexingConfigurationProvider _target;

    public IndexerDatabasePathResolutionTests()
    {
        _options = new IndexerOptions();
        _target = new OptionsIndexingConfigurationProvider(
            Options.Create(_options),
            new NuGetPackageOptionsLoader());
    }

    // Purpose: resolves a relative indexer database path from the executable directory
    [Fact]
    public void GetSettings_RelativeIndexerDatabasePath_ResolvesFromExecutableDirectory()
    {
        // arrange
        var relativePath = Path.Combine("data", "indexer.db");
        _options.DatabasePath = relativePath;

        // act
        var actual = _target.GetSettings();

        // assert
        Assert.Equal(
            Path.GetFullPath(relativePath, AppContext.BaseDirectory),
            actual.DatabasePath);
    }

    // Purpose: preserves an absolute indexer database path
    [Fact]
    public void GetSettings_AbsoluteIndexerDatabasePath_PreservesAbsolutePath()
    {
        // arrange
        var absolutePath = Path.Combine(Path.GetTempPath(), "indexer.db");
        _options.DatabasePath = absolutePath;

        // act
        var actual = _target.GetSettings();

        // assert
        Assert.Equal(Path.GetFullPath(absolutePath), actual.DatabasePath);
    }
}
