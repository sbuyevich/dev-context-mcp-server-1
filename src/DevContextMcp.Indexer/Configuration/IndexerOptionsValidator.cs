using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace DevContextMcp.Indexer.Configuration;

/// <summary>
/// Validates Indexer configuration before indexing starts.
/// </summary>
public sealed class IndexerOptionsValidator :
    IValidateOptions<IndexerOptions>
{
    private static readonly Regex EnvironmentPattern = new(
        "^[A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant);

    private readonly INuGetPackageOptionsLoader _packageOptionsLoader;

    public IndexerOptionsValidator()
        : this(new NuGetPackageOptionsLoader())
    {
    }

    internal IndexerOptionsValidator(INuGetPackageOptionsLoader packageOptionsLoader)
    {
        _packageOptionsLoader = packageOptionsLoader;
    }

    public ValidateOptionsResult Validate(string? name, IndexerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ConfigurationValidation.ValidatePath(
            options.DatabasePath,
            "DevContextMcp:DatabasePath",
            failures);
        ConfigurationValidation.ValidatePath(
            options.NugetsPath,
            "DevContextMcp:NugetsPath",
            failures);
        ValidateLimits(options.Indexing, failures);
        ValidateSourceNames(options, failures);
        ValidateEnvironments(options.Environments, failures);
        ValidateDocumentation(options.Documentation, failures);

        if (options.Environments.Count > 0
            && !string.IsNullOrWhiteSpace(options.NugetsPath))
        {
            ValidatePackages(options, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateDocumentation(
        DocumentationOptions? documentation,
        List<string> failures)
    {
        if (documentation is null)
        {
            return;
        }

        ConfigurationValidation.ValidatePath(
            documentation.RootPath,
            "DevContextMcp:Documentation:RootPath",
            failures);
        if (!string.IsNullOrWhiteSpace(documentation.RootPath))
        {
            try
            {
                var path = Path.GetFullPath(
                    documentation.RootPath,
                    AppContext.BaseDirectory);
                if (!Directory.Exists(path))
                {
                    failures.Add(
                        $"DevContextMcp:Documentation:RootPath directory '{path}' does not exist.");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                // ValidatePath reports the malformed path.
            }
        }

        if (documentation.Extensions.Count == 0)
        {
            failures.Add(
                "DevContextMcp:Documentation:Extensions must contain at least one extension.");
            return;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in documentation.Extensions)
        {
            var extension = configured.Trim();
            if (extension.Length == 0
                || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || extension.Contains('*', StringComparison.Ordinal)
                || extension.Contains('?', StringComparison.Ordinal)
                || extension.Contains(Path.DirectorySeparatorChar)
                || extension.Contains(Path.AltDirectorySeparatorChar))
            {
                failures.Add(
                    $"Documentation extension '{configured}' is invalid.");
                continue;
            }

            extension = extension.StartsWith('.') ? extension : $".{extension}";
            if (extension.Length == 1)
            {
                failures.Add(
                    $"Documentation extension '{configured}' is invalid.");
            }
            else if (!normalized.Add(extension))
            {
                failures.Add(
                    $"Documentation extension '{configured}' is configured more than once.");
            }
        }
    }

    private static void ValidateLimits(IndexingOptions options, List<string> failures)
    {
        if (options.MaxPackageBytes <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxPackageBytes must be positive.");
        }

        if (options.MaxDocumentBytes <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxDocumentBytes must be positive.");
        }

        if (options.MaxArchiveEntries <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxArchiveEntries must be positive.");
        }

        if (options.MaxExtractedBytes <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxExtractedBytes must be positive.");
        }

        if (!double.IsFinite(options.MaxCompressionRatio) || options.MaxCompressionRatio <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxCompressionRatio must be positive.");
        }

        if (options.MaxDocumentChars <= 0)
        {
            failures.Add("DevContextMcp:Indexing:MaxDocumentChars must be positive.");
        }

        if (options.PackageDownloadTimeout <= TimeSpan.Zero)
        {
            failures.Add("DevContextMcp:Indexing:PackageDownloadTimeout must be positive.");
        }
    }

    private static void ValidateSourceNames(
        IndexerOptions options,
        List<string> failures)
    {
        var names = options.Environments.Select(source => source.Name)
            .ToList();

        foreach (var name in names.Where(string.IsNullOrWhiteSpace))
        {
            failures.Add("Every configured source must have a non-empty name.");
        }

        var duplicates = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            failures.Add($"Source name '{duplicate}' is configured more than once.");
        }
    }

    private static void ValidateEnvironments(
        IEnumerable<NuGetEnvironmentOptions> sources,
        List<string> failures)
    {
        foreach (var source in sources)
        {
            if (!IsEnvironment(source.Name))
            {
                failures.Add(
                    $"NuGet source Name '{source.Name}' must contain only letters, numbers, '.', '_', or '-'.");
            }

            if (!ConfigurationValidation.IsNuGetSource(source.ServiceIndex))
            {
                failures.Add(
                    $"NuGet source '{source.Name}' must have an absolute HTTP/HTTPS ServiceIndex URI or a valid local path.");
            }

            if (source.MaxPackages <= 0)
            {
                failures.Add($"NuGet source '{source.Name}' MaxPackages must be positive.");
            }
        }
    }

    private void ValidatePackages(IndexerOptions options, List<string> failures)
    {
        IReadOnlyList<NuGetPackageOptions> packages;
        try
        {
            packages = _packageOptionsLoader.Load(options.NugetsPath);
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
            return;
        }

        foreach (var package in packages)
        {
            if (!IsEnvironment(package.Environment))
            {
                failures.Add(
                    $"NuGet package '{package.PackageId}' Environment must contain only letters, numbers, '.', '_', or '-'.");
            }

            if (string.IsNullOrWhiteSpace(package.PackageId))
            {
                failures.Add("Every NuGet package configuration must have a non-empty PackageId.");
            }

            if (!package.Delete && package.MaxVersionsPerPackage <= 0)
            {
                failures.Add(
                    $"NuGet package '{package.PackageId}' MaxVersionsPerPackage must be positive.");
            }
        }

        var duplicates = packages
            .Where(package =>
                !string.IsNullOrWhiteSpace(package.Environment)
                && !string.IsNullOrWhiteSpace(package.PackageId))
            .GroupBy(
                package => (package.Environment, package.PackageId),
                EnvironmentPackageComparer.Instance)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            failures.Add(
                $"NuGet package '{duplicate.Key.PackageId}' is configured more than once in environment '{duplicate.Key.Environment}'.");
        }

        var configuredEnvironments = options.Environments
            .Select(source => source.Name)
            .Where(environment => !string.IsNullOrWhiteSpace(environment))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages.Where(package =>
                     !string.IsNullOrWhiteSpace(package.Environment)
                     && !configuredEnvironments.Contains(package.Environment)))
        {
            failures.Add(
                $"NuGet package '{package.PackageId}' references undefined environment '{package.Environment}'.");
        }

        foreach (var source in options.Environments)
        {
            var count = packages.Count(package =>
                string.Equals(
                    package.Environment,
                    source.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (source.MaxPackages > 0 && count > source.MaxPackages)
            {
                failures.Add(
                    $"NuGet source '{source.Name}' matches {count} package configurations, exceeding MaxPackages {source.MaxPackages}.");
            }
        }
    }

    private static bool IsEnvironment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && EnvironmentPattern.IsMatch(value);

    private sealed class EnvironmentPackageComparer :
        IEqualityComparer<(string Environment, string PackageId)>
    {
        public static EnvironmentPackageComparer Instance { get; } = new();

        public bool Equals(
            (string Environment, string PackageId) x,
            (string Environment, string PackageId) y) =>
            string.Equals(x.Environment, y.Environment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Environment, string PackageId) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Environment),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId));
    }
}
