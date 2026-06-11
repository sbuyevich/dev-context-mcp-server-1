namespace DevContextMcp.Server.Configuration;

/// <summary>
/// Limits applied to indexed documentation 
/// </summary>
public sealed class RetrievalOptions
{
    public List<string> EnvironmentOrder { get; set; } = [];

    public List<string> SourceOrder { get; set; } = [];

    public int DefaultMaxResults { get; set; } = 8;

    public int MaxResults { get; set; } = 25;

    public int MaxResponseBytes { get; set; } = 100 * 1024;

    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public double MinimumEvidenceScore { get; set; } = 0.15;

    public int AmbiguousSymbolLimit { get; set; } = 10;
}
