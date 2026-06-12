using System.ComponentModel;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RetrievalConfigurationProvider = DevContextMcp.Server.Core.Services.IConfigurationProvider;

namespace DevContextMcp.Server.Resources;

[McpServerResourceType]
public sealed class NuGetResources(
    RetrievalConfigurationProvider configurationProvider,
    INuGetReadStore store,
    ICitationFactory citationFactory)
{
    [McpServerResource(
        UriTemplate = "nuget://{source}/{packageId}/{version}/artifact/{path}",
        Name = "NuGet package artifact")]
    [Description("Returns an exact indexed NuGet README, XML documentation, or text artifact.")]
    public async Task<ResourceContents> ReadArtifactAsync(
        string source,
        string packageId,
        string version,
        string path,
        CancellationToken cancellationToken)
    {
        var settings = configurationProvider.GetSettings();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Limits.QueryTimeout);
        var resource = await store.ReadArtifactAsync(
            settings.DatabasePath,
            Decode(source),
            Decode(packageId),
            Decode(version),
            Decode(path),
            timeout.Token);
        if (resource is null)
        {
            throw new McpException("The indexed NuGet artifact was not found.");
        }

        return new TextResourceContents
        {
            Uri = citationFactory.ArtifactUri(
                Decode(source),
                Decode(packageId),
                Decode(version),
                Decode(path)),
            MimeType = resource.MimeType,
            Text = resource.Text
        };
    }

    [McpServerResource(
        UriTemplate = "nuget://{source}/{packageId}/{version}/symbol/{qualifiedName}",
        Name = "NuGet package symbol")]
    [Description("Returns an exact indexed public NuGet symbol signature and documentation.")]
    public async Task<ResourceContents> ReadSymbolAsync(
        string source,
        string packageId,
        string version,
        string qualifiedName,
        CancellationToken cancellationToken)
    {
        var settings = configurationProvider.GetSettings();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Limits.QueryTimeout);
        var resource = await store.ReadSymbolAsync(
            settings.DatabasePath,
            Decode(source),
            Decode(packageId),
            Decode(version),
            Decode(qualifiedName),
            timeout.Token);
        if (resource is null)
        {
            throw new McpException("The indexed NuGet symbol was not found.");
        }

        return new TextResourceContents
        {
            Uri = citationFactory.SymbolUri(
                Decode(source),
                Decode(packageId),
                Decode(version),
                Decode(qualifiedName)),
            MimeType = resource.MimeType,
            Text = resource.Text
        };
    }

    private static string Decode(string value)
    {
        var decoded = Uri.UnescapeDataString(value);
        if (string.IsNullOrWhiteSpace(decoded)
            || decoded.IndexOf('\0') >= 0
            || decoded.Any(char.IsControl))
        {
            throw new McpException("The resource URI contains an invalid segment.");
        }

        return decoded;
    }
}
