using DevContextMcp.Infrastructure.Indexer.NuGet;
using NuGet.Versioning;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class MetadataSymbolExtractorTests
{
    [Fact]
    public void DeduplicatesPersistenceIdentities()
    {
        var assemblyPath = typeof(NuGetVersion).Assembly.Location;
        var symbols = MetadataSymbolExtractor.Extract(
            File.ReadAllBytes(assemblyPath),
            "lib/net8.0/NuGet.Versioning.dll");

        Assert.NotEmpty(symbols);
        Assert.All(
            symbols.GroupBy(symbol => (
                symbol.AssemblyPath,
                symbol.Kind,
                symbol.FullyQualifiedName,
                symbol.Signature)),
            group => Assert.Single(group));
    }
}
