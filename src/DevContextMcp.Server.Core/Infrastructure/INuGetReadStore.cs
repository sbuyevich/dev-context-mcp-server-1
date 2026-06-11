using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Infrastructure;

public interface INuGetReadStore
{
    Task<IReadOnlyList<LibraryCandidateRecord>> SearchLibrariesAsync(
        string databasePath,
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ResolvedLibraryRecord>> FindLibrariesAsync(
        string databasePath,
        string packageId,
        CancellationToken cancellationToken);

    Task<bool> EnvironmentExistsAsync(
        string databasePath,
        string environment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexedVersionRecord>> ListVersionsAsync(
        string databasePath,
        string libraryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentHitRecord>> SearchDocumentsAsync(
        string databasePath,
        string libraryVersionId,
        string question,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SymbolHitRecord>> SearchSymbolsAsync(
        string databasePath,
        string libraryVersionId,
        string query,
        string? targetFramework,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SymbolHitRecord>> GetRelatedSymbolsAsync(
        string databasePath,
        string libraryVersionId,
        string containingType,
        string fullyQualifiedName,
        int limit,
        CancellationToken cancellationToken);

    Task<ResourceDocumentRecord?> ReadArtifactAsync(
        string databasePath,
        string sourceName,
        string packageId,
        string version,
        string path,
        CancellationToken cancellationToken);

    Task<ResourceDocumentRecord?> ReadSymbolAsync(
        string databasePath,
        string sourceName,
        string packageId,
        string version,
        string qualifiedName,
        CancellationToken cancellationToken);
}
