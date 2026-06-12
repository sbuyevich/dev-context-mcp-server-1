using DevContextMcp.Server;
using DevContextMcp.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

var bootstrapConfiguration = new ConfigurationManager();
bootstrapConfiguration.SetBasePath(AppContext.BaseDirectory);
bootstrapConfiguration.AddJsonFile("appsettings.json", optional: true);
bootstrapConfiguration.AddEnvironmentVariables();
bootstrapConfiguration.AddCommandLine(args);

var transport = bootstrapConfiguration[
    $"{DevContextMcpOptions.SectionName}:Transport"] ?? "stdio";

if (transport.Equals("http", StringComparison.Ordinal))
{
    await RunHttpAsync(args);
}
else
{
    await RunStdioAsync(args);
}

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(
        new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

    ConfigureLogging(builder.Services);
    builder.Services.AddDevContextMcpCore(builder.Configuration);
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithDevContextMcpTools();

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(
        new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

    ConfigureLogging(builder.Services);
    builder.Services.AddDevContextMcpCore(builder.Configuration);
    builder.Services.AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .WithDevContextMcpTools();

    var app = builder.Build();
    var options = app.Services
        .GetRequiredService<IOptions<DevContextMcpOptions>>()
        .Value;
    app.MapMcp(options.Http.Path);
    await app.RunAsync(options.Http.Url);
}

static void ConfigureLogging(IServiceCollection services)
{
    services.AddSerilog((registeredServices, configuration) => configuration
        .ReadFrom.Services(registeredServices)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            standardErrorFromLevel: LogEventLevel.Verbose,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            LogPath("server-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
}

static string LogPath(string fileName) =>
    Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "..", "data", "logs", fileName),
        AppContext.BaseDirectory);

public partial class Program;
