using DevContextMcp.Server;
using DevContextMcp.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog;

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

    ConfigureLogging(builder.Services, builder.Configuration);
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

    ConfigureLogging(builder.Services, builder.Configuration);
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

static void ConfigureLogging(
    IServiceCollection services,
    IConfiguration configuration)
{
    ApplyServerLoggingRequirements(configuration);
    ResolveRelativeFileSinkPaths(configuration);
    services.AddSerilog((registeredServices, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(configuration)
        .ReadFrom.Services(registeredServices));
}

static void ApplyServerLoggingRequirements(IConfiguration configuration)
{
    configuration[
        "Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime"] = "Information";

    foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
    {
        if (string.Equals(sink["Name"], "Console", StringComparison.OrdinalIgnoreCase))
        {
            sink["Args:standardErrorFromLevel"] = "Verbose";
        }
    }
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
