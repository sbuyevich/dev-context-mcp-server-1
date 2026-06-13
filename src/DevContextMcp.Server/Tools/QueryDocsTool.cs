using System.ComponentModel;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using ModelContextProtocol.Server;

namespace DevContextMcp.Server.Tools;

[McpServerToolType]
internal sealed class QueryDocsTool(
    IQueryDocsHandler handler,
    ToolInvocationLogger invocationLogger)
{
    [McpServerTool(
        Name = "query_docs",
        UseStructuredContent = true,
        OutputSchemaType = typeof(QueryDocsResponse))]
    [Description("Finds indexed documentation and examples for one internal library.")]
    public Task<QueryDocsResponse> QueryDocsAsync(
        [Description("Stable library identifier returned by resolve_library.")] string libraryId,
        [Description("Concrete implementation question to answer from indexed evidence.")] string question,
        [Description("Exact package or client version.")] string? version = null,
        [Description("Target framework used by the calling project, such as net10.0.")] string? targetFramework = null,
        [Description("Maximum number of evidence results to return.")] int maxResults = 8,
        [Description("Package version referenced by the calling project.")] string? projectVersion = null,
        [Description("Whether prerelease versions may be selected.")] bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var request = new QueryDocsRequest(
            libraryId,
            question,
            version,
            targetFramework,
            maxResults,
            projectVersion,
            includePrerelease);

        return invocationLogger.InvokeAsync(
            "query_docs",
            request,
            token => handler.HandleAsync(request, token),
            cancellationToken);
    }
}
