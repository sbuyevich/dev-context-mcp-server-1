using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

internal sealed class RetrievalLibraryResolver(
    INuGetReadStore store,
    IVersionResolver versionResolver) :
    ILibraryResolver
{
    public async Task<LibraryResolutionResult> ResolveAsync(
        string databasePath,
        LibraryId libraryId,
        IReadOnlyList<string> environmentOrder,
        IReadOnlyList<string> sourceOrder,
        IReadOnlyDictionary<string, string> recommendedVersions,
        string? requestedVersion,
        string? projectVersion,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        if (libraryId.Environment is not null
            && !await store.EnvironmentExistsAsync(
                databasePath,
                libraryId.Environment,
                cancellationToken))
        {
            return new(LibraryResolutionStatus.EnvironmentNotFound);
        }

        var libraries = await store.FindLibrariesAsync(
            databasePath,
            libraryId.PackageId,
            cancellationToken);
        var matchingLibraries = libraryId.Environment is null
            ? libraries
            : libraries
                .Where(library => library.Environment.Equals(
                    libraryId.Environment,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (matchingLibraries.Count == 0)
        {
            return new(LibraryResolutionStatus.LibraryNotFound);
        }

        var candidates = new List<ResolvedLibrarySelection>();
        foreach (var library in matchingLibraries)
        {
            var versions = await store.ListVersionsAsync(
                databasePath,
                library.LibraryId,
                cancellationToken);
            var recommendation = RecommendedVersionSelector.Find(
                recommendedVersions,
                library.Environment,
                library.PackageId);
            candidates.Add(new(
                library,
                versions,
                versionResolver.Resolve(
                    versions,
                    requestedVersion,
                    projectVersion,
                    recommendation,
                    includePrerelease)));
        }

        var selected = candidates
            .OrderBy(candidate => candidate.Version is null)
            .ThenBy(candidate => OrderIndex(environmentOrder, candidate.Library.Environment))
            .ThenBy(candidate => candidate.Version?.WarningCodes.Contains(
                "recommended_version_not_indexed",
                StringComparer.Ordinal) ?? false)
            .ThenBy(candidate => OrderIndex(sourceOrder, candidate.Library.SourceName))
            .ThenBy(candidate => candidate.Library.Environment, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Library.SourceName, StringComparer.Ordinal)
            .First();

        return new(LibraryResolutionStatus.Resolved, selected);
    }

    internal static int OrderIndex(IReadOnlyList<string> order, string value)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index].Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
