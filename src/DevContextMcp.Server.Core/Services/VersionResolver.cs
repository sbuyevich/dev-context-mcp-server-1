using DevContextMcp.Server.Core.Models;
using NuGet.Versioning;

namespace DevContextMcp.Server.Core.Services;

internal sealed class VersionResolver : IVersionResolver
{
    public VersionResolution? Resolve(
        IReadOnlyList<IndexedVersionRecord> versions,
        string? requestedVersion,
        string? projectVersion,
        string? recommendedVersion,
        bool includePrerelease)
    {
        var parsed = versions
            .Select(version => (Record: version, Parsed: TryParse(version.Version)))
            .Where(item => item.Parsed is not null)
            .Select(item => (item.Record, Parsed: item.Parsed!))
            .OrderByDescending(item => item.Parsed, VersionComparer.VersionRelease)
            .ToArray();

        var explicitVersion = SelectExact(parsed, requestedVersion);
        if (requestedVersion is not null)
        {
            return explicitVersion is null
                ? null
                : new(explicitVersion, "requested", []);
        }

        var project = SelectExact(parsed, projectVersion);
        if (projectVersion is not null)
        {
            return project is null ? null : new(project, "project_context", []);
        }

        var warnings = new List<string>();
        var recommended = SelectExact(parsed, recommendedVersion);
        if (recommendedVersion is not null)
        {
            if (recommended is not null
                && (includePrerelease || !recommended.Prerelease))
            {
                return new(recommended, "configured_recommendation", warnings);
            }

            warnings.Add("recommended_version_not_indexed");
        }

        var stable = parsed
            .Select(item => item.Record)
            .FirstOrDefault(version => version.Listed && !version.Prerelease);
        if (stable is not null)
        {
            return new(stable, "latest_stable", warnings);
        }

        var prerelease = includePrerelease
            ? parsed.Select(item => item.Record).FirstOrDefault(version => version.Listed)
            : null;
        return prerelease is null
            ? null
            : new(prerelease, "latest_prerelease", warnings);
    }

    private static IndexedVersionRecord? SelectExact(
        IEnumerable<(IndexedVersionRecord Record, NuGetVersion Parsed)> versions,
        string? value)
    {
        if (value is null || !NuGetVersion.TryParse(value, out var parsed))
        {
            return null;
        }

        return versions
            .FirstOrDefault(item => VersionComparer.VersionRelease.Equals(item.Parsed, parsed))
            .Record;
    }

    private static NuGetVersion? TryParse(string value) =>
        NuGetVersion.TryParse(value, out var version) ? version : null;
}
