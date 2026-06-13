using DevContextMcp.Indexer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

ResolveRelativeFileSinkPaths(builder.Configuration);
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));
builder.Services.AddIndexerCli(builder.Configuration);

using var host = builder.Build();

try
{
    await host.StartAsync();
    var applicationLifetime = host.Services
        .GetRequiredService<IHostApplicationLifetime>();
    var succeeded = await host.Services
        .GetRequiredService<IndexerRunner>()
        .RunAsync(applicationLifetime.ApplicationStopping);
    await host.StopAsync();
    return succeeded ? 0 : 1;
}
catch (OperationCanceledException)
{
    return 1;
}
catch (Exception exception)
{
    host.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DevContextMcp.Indexer")
        .LogError(exception, "Indexing failed.");
    return 1;
}

static void ResolveRelativeFileSinkPaths(IConfiguration configuration)
{
    foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
    {
        if (!string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var path = sink["Args:path"];
        if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
        {
            sink["Args:path"] = Path.GetFullPath(path, AppContext.BaseDirectory);
        }
    }
}

public partial class Program;
