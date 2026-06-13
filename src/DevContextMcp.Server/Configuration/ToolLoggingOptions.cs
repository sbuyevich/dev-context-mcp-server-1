namespace DevContextMcp.Server.Configuration;

/// <summary>
/// Diagnostic payload limits for MCP tool invocation logging.
/// </summary>
public sealed class ToolLoggingOptions
{
    public int MaxPayloadBytes { get; set; } = 32 * 1024;
}
