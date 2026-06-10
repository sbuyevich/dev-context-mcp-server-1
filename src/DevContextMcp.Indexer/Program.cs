using DevContextMcp.Indexer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
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

public partial class Program;
