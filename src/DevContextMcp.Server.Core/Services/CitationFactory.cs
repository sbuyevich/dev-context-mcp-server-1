namespace DevContextMcp.Server.Core.Services;

internal sealed class CitationFactory : ICitationFactory
{
    public string ArtifactUri(
        string source,
        string packageId,
        string version,
        string path) =>
        $"nuget://{Escape(source)}/{Escape(packageId)}/{Escape(version)}/artifact/{EscapePath(path)}";

    public string SymbolUri(
        string source,
        string packageId,
        string version,
        string qualifiedName) =>
        $"nuget://{Escape(source)}/{Escape(packageId)}/{Escape(version)}/symbol/{Escape(qualifiedName)}";

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string EscapePath(string path) => Escape(path.Replace('\\', '/'));
}
