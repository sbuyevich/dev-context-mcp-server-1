using DevContextMcp.Server.Core.Models;
using DevContextMcp.Server.Configuration;
using Microsoft.Extensions.Options;
using RetrievalConfigurationProvider = DevContextMcp.Server.Core.Services.IConfigurationProvider;

namespace DevContextMcp.Server.Retrieval;

internal sealed class OptionsRetrievalConfigurationProvider(
    IOptions<DevContextMcpOptions> options) : RetrievalConfigurationProvider
{
    public RetrievalSettings GetSettings()
    {
        var value = options.Value;
        var retrieval = value.Retrieval;

        return new(
            Path.GetFullPath(value.DatabasePath, AppContext.BaseDirectory),
            retrieval.EnvironmentOrder.ToArray(),
            retrieval.SourceOrder.ToArray(),
            new Dictionary<string, string>(
                value.RecommendedVersions,
                StringComparer.OrdinalIgnoreCase),
            new RetrievalLimits(
                retrieval.DefaultMaxResults,
                retrieval.MaxResults,
                retrieval.MaxResponseBytes,
                retrieval.QueryTimeout,
                retrieval.MinimumEvidenceScore,
                retrieval.AmbiguousSymbolLimit));
    }
}
