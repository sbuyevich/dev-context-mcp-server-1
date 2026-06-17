namespace DevContextMcp.Server.Configuration;

/// <summary>
/// Root configuration for the MCP documentation server.
/// </summary>
public sealed class DevContextMcpOptions
{
    public const string SectionName = "DevContextMcp";

    public string McpUrl { get; set; } = "http://127.0.0.1:5034/mcp";

    public string DatabasePath { get; set; } = "data/docs.db";

    public RetrievalOptions Retrieval { get; set; } = new();

    public ToolLoggingOptions ToolLogging { get; set; } = new();
}
