using System.ComponentModel;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using ModelContextProtocol.Server;

namespace DevContextMcp.Server.Tools;

[McpServerToolType]
internal sealed class GetSymbolTool(
    IGetSymbolHandler handler,
    ToolInvocationLogger invocationLogger)
{
    [McpServerTool(
        Name = "get_symbol",
        UseStructuredContent = true,
        OutputSchemaType = typeof(GetSymbolResponse))]
    [Description("Finds and describes a public type or member in an internal library.")]
    public Task<GetSymbolResponse> GetSymbolAsync(
        [Description("Stable library identifier returned by resolve_library.")] string libraryId,
        [Description("Fully qualified, simple, or partial symbol name.")] string symbol,
        [Description("Exact package or client version.")] string? version = null,
        [Description("Target framework used by the calling project, such as net10.0.")] string? targetFramework = null,
        [Description("Package version referenced by the calling project.")] string? projectVersion = null,
        [Description("Whether prerelease versions may be selected.")] bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var request = new GetSymbolRequest(
            libraryId,
            symbol,
            version,
            targetFramework,
            projectVersion,
            includePrerelease);

        return invocationLogger.InvokeAsync(
            "get_symbol",
            request,
            token => handler.HandleAsync(request, token),
            cancellationToken);
    }
}
