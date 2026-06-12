using System.ComponentModel;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using ModelContextProtocol.Server;

namespace DevContextMcp.Server.Tools;

[McpServerToolType]
public sealed class ListVersionsTool(IListVersionsHandler handler)
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
        return handler.HandleAsync(
            new ListVersionsRequest(libraryId, includePrerelease),
            cancellationToken);
    }
}
