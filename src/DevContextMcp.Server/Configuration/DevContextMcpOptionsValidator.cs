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
        ValidateMcpUrl(options, failures);
        ConfigurationValidation.ValidatePath(
            options.DatabasePath,
            "DevContextMcp:DatabasePath",
            failures);
        ValidateRetrieval(options.Retrieval, failures);
        ValidateToolLogging(options.ToolLogging, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateMcpUrl(
        DevContextMcpOptions options,
        List<string> failures)
    {
        if (!Uri.TryCreate(options.McpUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.AbsolutePath == "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            failures.Add(
                "DevContextMcp:McpUrl must be an absolute HTTP loopback URL with an endpoint path and without a query, fragment, or credentials.");
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


    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPattern();
}
