using DevContextMcp.Indexer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Services(services)
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        LogPath("indexer-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
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

static string LogPath(string fileName) =>
    Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "..", "data", "logs", fileName),
        AppContext.BaseDirectory);

public partial class Program;
