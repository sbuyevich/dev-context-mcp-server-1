using DevContextMcp.Server.Core.Models;
using DevContextMcp.Server.Core.Services;

namespace DevContextMcp.UnitTests.Retrieval;

public sealed class VersionResolverTests
{
    private readonly VersionResolver _resolver = new();

    [Fact]
    public void RequestedVersionWins()
    {
        var result = _resolver.Resolve(Versions(), "1.0.0", "2.0.0", "2.0.0", true);

        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.Version.Version);
        Assert.Equal("requested", result.Reason);
    }

    [Fact]
    public void RecommendationWinsWhenNoContextVersionExists()
    {
        var result = _resolver.Resolve(Versions(), null, null, "2.0.0", false);

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version.Version);
        Assert.Equal("configured_recommendation", result.Reason);
    }

    [Fact]
    public void LatestStableUsesSemanticVersionOrdering()
    {
        var versions = Versions()
            .Append(new("four", "10.0.0", true, false, false, null))
            .ToArray();

        var result = _resolver.Resolve(versions, null, null, null, false);

        Assert.NotNull(result);
        Assert.Equal("10.0.0", result.Version.Version);
    }

    [Fact]
    public void PrereleaseRequiresPermission()
    {
        var prerelease = new[]
        {
            new IndexedVersionRecord("one", "3.0.0-beta.1", true, true, false, null)
        };

        Assert.Null(_resolver.Resolve(prerelease, null, null, null, false));
        Assert.Equal(
            "3.0.0-beta.1",
            _resolver.Resolve(prerelease, null, null, null, true)!.Version.Version);
    }

    private static IReadOnlyList<IndexedVersionRecord> Versions() =>
    [
        new("one", "1.0.0", true, false, false, null),
        new("two", "2.0.0", true, false, false, null),
        new("three", "3.0.0-beta.1", true, true, false, null)
    ];
}
