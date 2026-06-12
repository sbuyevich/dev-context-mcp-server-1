namespace DevContextMcp.Indexer.Core.Infrastructure;

public interface INuGetSourceAuthenticationProvider
{
    void Configure(object packageSource, string sourceName);
}
