using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

public interface ILibraryResolver
{
    Task<LibraryResolutionResult> ResolveAsync(
        string databasePath,
        LibraryId libraryId,
        IReadOnlyList<string> environmentOrder,
        IReadOnlyList<string> sourceOrder,
        IReadOnlyDictionary<string, string> recommendedVersions,
        string? requestedVersion,
        string? projectVersion,
        bool includePrerelease,
        CancellationToken cancellationToken);
}
