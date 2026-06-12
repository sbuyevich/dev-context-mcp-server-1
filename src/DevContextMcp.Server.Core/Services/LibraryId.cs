using System.Text.RegularExpressions;

namespace DevContextMcp.Server.Core.Services;

public readonly partial record struct LibraryId(
    string PackageId,
    string? Environment = null)
{
    private const string Prefix = "nuget:";

    public static bool TryParse(string value, out LibraryId libraryId)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && value.Length > Prefix.Length)
        {
            var payload = value[Prefix.Length..].Trim();
            var separator = payload.IndexOf('/');
            if (separator < 0)
            {
                libraryId = new(payload);
                return libraryId.PackageId.Length > 0;
            }

            if (separator == 0
                || separator == payload.Length - 1
                || payload.IndexOf('/', separator + 1) >= 0)
            {
                libraryId = default;
                return false;
            }

            var environment = payload[..separator];
            var packageId = payload[(separator + 1)..].Trim();
            if (!IsValidEnvironment(environment) || packageId.Length == 0)
            {
                libraryId = default;
                return false;
            }

            libraryId = new(packageId, environment);
            return true;
        }

        libraryId = default;
        return false;
    }

    public static bool IsValidEnvironment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && EnvironmentPattern().IsMatch(value);

    public override string ToString() =>
        Environment is null
            ? $"{Prefix}{PackageId}"
            : $"{Prefix}{Environment}/{PackageId}";

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPattern();
}
