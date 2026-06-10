namespace DevContextMcp.Indexer.Cli.Configuration;

/// <summary>
/// Package-specific NuGet indexing policy loaded from an external JSON file.
/// </summary>
public sealed class NuGetPackageOptions
{
    public string Environment { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public int MaxVersionsPerPackage { get; set; } = 3;

    public bool IncludePrerelease { get; set; }

    public bool IncludeUnlisted { get; set; }
}
