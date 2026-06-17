using System.ComponentModel;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Resources;

[McpServerResourceType]
public sealed class DocumentationResources(
    RetrievalSettings settings,
    INuGetReadStore store,
    ICitationFactory citationFactory)
{
    [McpServerResource(
        UriTemplate = "docs://company-docs/{path}",
        Name = "Company documentation file")]
    [Description("Returns one complete indexed company documentation file.")]
    public async Task<ResourceContents> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var decodedPath = DecodePath(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Limits.QueryTimeout);
        var resource = await store.ReadDocumentationAsync(
            settings.DatabasePath,
            decodedPath,
            timeout.Token);
        if (resource is null)
        {
            throw new McpException("The indexed company documentation file was not found.");
        }

        return new TextResourceContents
        {
            Uri = citationFactory.DocumentationUri(decodedPath),
            MimeType = resource.MimeType,
            Text = resource.Text
        };
    }

    private static string DecodePath(string value)
    {
        var decoded = Uri.UnescapeDataString(value).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(decoded)
            || decoded.IndexOf('\0') >= 0
            || decoded.Any(char.IsControl)
            || decoded.StartsWith("/", StringComparison.Ordinal)
            || decoded.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new McpException("The resource URI contains an invalid path.");
        }

        return decoded;
    }
}
