using DevContextMcp.Infrastructure.Server;

namespace DevContextMcp.UnitTests.Retrieval;

public sealed class FtsQueryBuilderTests
{
    [Fact]
    public void BuildsQuotedPrefixQueryFromUntrustedText()
    {
        var query = FtsQueryBuilder.Build("customer OR \"drop table\"");

        Assert.Equal(
            "\"customer\"* AND \"OR\"* AND \"drop\"* AND \"table\"*",
            query);
        Assert.DoesNotContain(';', query);
    }
}
