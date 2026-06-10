namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// Approved NuGet v3 feed configuration.
/// </summary>
public sealed class NuGetEnvironmentOptions
{
    public string Name { get; set; } = string.Empty;

    public string ServiceIndex { get; set; } = string.Empty;

    public int MaxPackages { get; set; } = 100;
}
