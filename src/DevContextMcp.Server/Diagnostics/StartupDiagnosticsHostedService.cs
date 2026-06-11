using DevContextMcp.Server.Core.Diagnostics;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetrievalConfigurationProvider = DevContextMcp.Server.Core.Services.IConfigurationProvider;

namespace DevContextMcp.Server.Diagnostics;

internal sealed class StartupDiagnosticsHostedService(
    RetrievalConfigurationProvider retrievalConfigurationProvider,
    ToolRegistrationCatalog toolCatalog,
    ILocalDependencyCheck localDependencyCheck,
    IResolveLibraryHandler resolveLibraryHandler,
    IQueryDocsHandler queryDocsHandler,
    IGetSymbolHandler getSymbolHandler,
    IListVersionsHandler listVersionsHandler,
    ILogger<StartupDiagnosticsHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = resolveLibraryHandler;
        _ = queryDocsHandler;
        _ = getSymbolHandler;
        _ = listVersionsHandler;

        if (!toolCatalog.Names.SequenceEqual(ToolRegistrationCatalog.ExpectedNames, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The registered MCP tool catalog is incomplete.");
        }

        var settings = retrievalConfigurationProvider.GetSettings();
        var localResults = await localDependencyCheck.CheckAsync(
            settings.DatabasePath,
            cancellationToken);

        foreach (var result in localResults)
        {
            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Startup diagnostic {CheckName} succeeded: {Message}",
                    result.Name,
                    result.Message);
            }
            else
            {
                logger.LogError(
                    "Startup diagnostic {CheckName} failed: {Message}",
                    result.Name,
                    result.Message);
            }
        }

        if (localResults.Any(result => !result.Succeeded))
        {
            throw new InvalidOperationException("One or more startup diagnostics failed.");
        }

        logger.LogInformation(
            "MCP documentation server startup checks completed. ToolCount={ToolCount}, LocalCheckCount={LocalCheckCount}, DatabasePath={DatabasePath}",
            toolCatalog.Names.Count,
            localResults.Count,
            settings.DatabasePath);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
