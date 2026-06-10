using DevContextMcp.Infrastructure.Indexer.Abstractions;

namespace DevContextMcp.Infrastructure.Indexer.NuGet;

internal sealed class AnonymousNuGetSourceAuthenticationProvider :
    INuGetSourceAuthenticationProvider
{
    public void Configure(object packageSource, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(packageSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
    }
}
