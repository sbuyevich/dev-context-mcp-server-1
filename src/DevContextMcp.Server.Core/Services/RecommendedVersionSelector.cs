namespace DevContextMcp.Server.Core.Services;

internal static class RecommendedVersionSelector
{
    public static string? Find(
        IReadOnlyDictionary<string, string> recommendedVersions,
        string environment,
        string packageId)
    {
        var qualifiedId = new LibraryId(packageId, environment).ToString();
        return recommendedVersions.TryGetValue(qualifiedId, out var qualified)
            ? qualified
            : recommendedVersions.TryGetValue(packageId, out var packageWide)
                ? packageWide
                : null;
    }
}
