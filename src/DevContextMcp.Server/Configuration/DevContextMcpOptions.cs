namespace DevContextMcp.Server.Configuration;

/// <summary>
/// Root configuration for the MCP documentation server.
/// </summary>
public sealed class DevContextMcpOptions
{
    public const string SectionName = "DevContextMcp";

    public string Transport { get; set; } = "stdio";

    public HttpHostOptions Http { get; set; } = new();

    public string DatabasePath { get; set; } = "data/docs.db";

    public Dictionary<string, string> RecommendedVersions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public RetrievalOptions Retrieval { get; set; } = new();

    public ToolLoggingOptions ToolLogging { get; set; } = new();
}
