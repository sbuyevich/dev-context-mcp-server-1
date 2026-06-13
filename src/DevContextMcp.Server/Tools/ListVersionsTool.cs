using System.ComponentModel;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using ModelContextProtocol.Server;

namespace DevContextMcp.Server.Tools;

[McpServerToolType]
internal sealed class ListVersionsTool(
    IListVersionsHandler handler,
    ToolInvocationLogger invocationLogger)
{
    [McpServerTool(
        Name = "list_versions",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ListVersionsResponse))]
    [Description("Lists indexed versions and identifies the recommended version for a library.")]
    public Task<ListVersionsResponse> ListVersionsAsync(
        [Description("Stable library identifier returned by resolve_library.")] string libraryId,
        [Description("Whether prerelease package versions should be included.")] bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest(libraryId, includePrerelease);

        return invocationLogger.InvokeAsync(
            "list_versions",
            request,
            token => handler.HandleAsync(request, token),
            cancellationToken);
    }
}
