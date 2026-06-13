using DevContextMcp.Infrastructure;
using DevContextMcp.Server.Configuration;
using DevContextMcp.Server.Core;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Diagnostics;
using DevContextMcp.Server.Resources;
using DevContextMcp.Server.Retrieval;
using DevContextMcp.Server.Tools;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Server;

/// <summary>
/// Host service registration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDevContextMcpCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DevContextMcpOptions>, DevContextMcpOptionsValidator>();
        var optionsSection = configuration.GetSection(DevContextMcpOptions.SectionName);
        services.AddOptions<DevContextMcpOptions>()
            .Bind(optionsSection)
            .PostConfigure(options =>
                options.RecommendedVersions =
                    RecommendedVersionsConfigurationReader.Read(
                        optionsSection.GetSection(nameof(options.RecommendedVersions))))
            .ValidateOnStart();

        services.AddApplication();
        services.AddRetrievalInfrastructure();
        services.AddSingleton<Core.Services.IConfigurationProvider>(provider =>
            new OptionsRetrievalConfigurationProvider(
                provider.GetRequiredService<IOptions<DevContextMcpOptions>>()));
        services.AddSingleton<ToolRegistrationCatalog>();
        services.AddSingleton<ToolInvocationLogger>();
        services.AddHostedService<StartupDiagnosticsHostedService>();

        return services;
    }

    public static IMcpServerBuilder WithDevContextMcpTools(this IMcpServerBuilder builder)
    {
        return builder
            .WithTools<ResolveLibraryTool>()
            .WithTools<QueryDocsTool>()
            .WithTools<GetSymbolTool>()
            .WithTools<ListVersionsTool>()
            .WithResources<NuGetResources>()
            .WithResources<DocumentationResources>();
    }
}
