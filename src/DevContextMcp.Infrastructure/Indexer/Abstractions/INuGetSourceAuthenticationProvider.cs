namespace DevContextMcp.Infrastructure.Indexer.Abstractions;

internal interface INuGetSourceAuthenticationProvider
{
    void Configure(object packageSource, string sourceName);
}
