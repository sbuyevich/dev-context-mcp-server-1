using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Server.Configuration;

/// <summary>
/// Validates retrieval host configuration before the MCP transport starts.
/// </summary>
public sealed partial class DevContextMcpOptionsValidator : IValidateOptions<DevContextMcpOptions>
{
    public ValidateOptionsResult Validate(string? name, DevContextMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateTransport(options, failures);
        ConfigurationValidation.ValidatePath(
            options.DatabasePath,
            "DevContextMcp:DatabasePath",
            failures);
        ValidateRetrieval(options.Retrieval, failures);
        ValidateToolLogging(options.ToolLogging, failures);
        ValidateRecommendedVersions(options.RecommendedVersions, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateTransport(
        DevContextMcpOptions options,
        List<string> failures)
    {
        if (options.Transport is not ("stdio" or "http"))
        {
            failures.Add("DevContextMcp:Transport must be 'stdio' or 'http'.");
        }

        if (!Uri.TryCreate(options.Http.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            failures.Add(
                "DevContextMcp:Http:Url must be an absolute HTTP loopback URL without a path, query, fragment, or credentials.");
        }

        if (string.IsNullOrWhiteSpace(options.Http.Path)
            || !options.Http.Path.StartsWith("/", StringComparison.Ordinal)
            || options.Http.Path.Contains("?", StringComparison.Ordinal)
            || options.Http.Path.Contains("#", StringComparison.Ordinal))
        {
            failures.Add(
                "DevContextMcp:Http:Path must start with '/' and must not contain a query or fragment.");
        }
    }

    private static void ValidateRetrieval(RetrievalOptions options, List<string> failures)
    {
        ValidateOrder(
            options.EnvironmentOrder,
            "DevContextMcp:Retrieval:EnvironmentOrder",
            validateEnvironment: true,
            failures);
        ValidateOrder(
            options.SourceOrder,
            "DevContextMcp:Retrieval:SourceOrder",
            validateEnvironment: false,
            failures);

        if (options.DefaultMaxResults <= 0)
        {
            failures.Add("DevContextMcp:Retrieval:DefaultMaxResults must be positive.");
        }

        if (options.MaxResults <= 0 || options.MaxResults < options.DefaultMaxResults)
        {
            failures.Add(
                "DevContextMcp:Retrieval:MaxResults must be positive and at least DefaultMaxResults.");
        }

        if (options.MaxResponseBytes <= 0)
        {
            failures.Add("DevContextMcp:Retrieval:MaxResponseBytes must be positive.");
        }

        if (options.QueryTimeout <= TimeSpan.Zero)
        {
            failures.Add("DevContextMcp:Retrieval:QueryTimeout must be positive.");
        }

        if (!double.IsFinite(options.MinimumEvidenceScore)
            || options.MinimumEvidenceScore is < 0 or > 1)
        {
            failures.Add(
                "DevContextMcp:Retrieval:MinimumEvidenceScore must be between 0 and 1.");
        }

        if (options.AmbiguousSymbolLimit <= 0)
        {
            failures.Add("DevContextMcp:Retrieval:AmbiguousSymbolLimit must be positive.");
        }
    }

    private static void ValidateToolLogging(
        ToolLoggingOptions options,
        List<string> failures)
    {
        if (options.MaxPayloadBytes <= 0)
        {
            failures.Add(
                "DevContextMcp:ToolLogging:MaxPayloadBytes must be positive.");
        }
    }

    private static void ValidateOrder(
        IReadOnlyList<string> values,
        string path,
        bool validateEnvironment,
        List<string> failures)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{path} contains an empty value.");
        }

        if (validateEnvironment
            && values.Any(value =>
                !string.IsNullOrWhiteSpace(value)
                && !EnvironmentPattern().IsMatch(value)))
        {
            failures.Add(
                $"{path} values must contain only letters, numbers, '.', '_', or '-'.");
        }

        var duplicate = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            failures.Add($"{path} contains duplicate value '{duplicate.Key}'.");
        }
    }

    private static void ValidateRecommendedVersions(
        IReadOnlyDictionary<string, string> versions,
        List<string> failures)
    {
        foreach (var (packageId, version) in versions)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                failures.Add("RecommendedVersions contains an empty package ID.");
            }
            else if (packageId.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = packageId["nuget:".Length..];
                var separator = payload.IndexOf('/');
                if (separator <= 0
                    || separator == payload.Length - 1
                    || payload.IndexOf('/', separator + 1) >= 0
                    || !EnvironmentPattern().IsMatch(payload[..separator]))
                {
                    failures.Add(
                        $"RecommendedVersions key '{packageId}' is not a valid environment-qualified library ID.");
                }
            }

            if (string.IsNullOrWhiteSpace(version) || !SemanticVersionPattern().IsMatch(version))
            {
                failures.Add(
                    $"Recommended version '{version}' for package '{packageId}' is not a valid semantic version.");
            }
        }
    }

    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPattern();
}
