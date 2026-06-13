using DevContextMcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DevContextMcp.UnitTests.Configuration;

public sealed class DevContextMcpOptionsValidatorTests
{
    private readonly DevContextMcpOptionsValidator _validator = new();

    [Fact]
    public void DefaultOptionsAreValid()
    {
        var result = _validator.Validate(null, new DevContextMcpOptions());

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Theory]
    [InlineData("stdio")]
    [InlineData("http")]
    public void SupportedTransportIsValid(string transport)
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions { Transport = transport });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void UnsupportedTransportFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions { Transport = "websocket" });

        AssertFailure(result, "Transport");
    }

    [Theory]
    [InlineData("https://127.0.0.1:5034")]
    [InlineData("http://0.0.0.0:5034")]
    [InlineData("http://example.com:5034")]
    [InlineData("not-a-url")]
    public void UnsafeHttpUrlFails(string url)
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                Http = new HttpHostOptions { Url = url }
            });

        AssertFailure(result, "Http:Url");
    }

    [Theory]
    [InlineData("")]
    [InlineData("mcp")]
    [InlineData("/mcp?mode=test")]
    [InlineData("/mcp#fragment")]
    public void InvalidHttpPathFails(string path)
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                Http = new HttpHostOptions { Path = path }
            });

        AssertFailure(result, "Http:Path");
    }

    [Fact]
    public void EmptyDatabasePathFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions { DatabasePath = " " });

        AssertFailure(result, "DatabasePath");
    }

    [Fact]
    public void InvalidRetrievalValuesFail()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                Retrieval = new RetrievalOptions
                {
                    EnvironmentOrder = ["qa", "QA"],
                    SourceOrder = ["nuget.org", "NuGet.org"],
                    DefaultMaxResults = 0
                }
            });

        AssertFailure(result, "EnvironmentOrder");
        AssertFailure(result, "SourceOrder");
        AssertFailure(result, "DefaultMaxResults");
    }

    [Fact]
    public void InvalidEnvironmentOrderSlugFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                Retrieval = new RetrievalOptions
                {
                    EnvironmentOrder = ["quality assurance"]
                }
            });

        AssertFailure(result, "EnvironmentOrder");
    }

    [Fact]
    public void InvalidRecommendedVersionFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                RecommendedVersions = new Dictionary<string, string>
                {
                    ["Company.Customer"] = "four"
                }
            });

        AssertFailure(result, "valid semantic version");
    }

    [Fact]
    public void NonPositiveToolLoggingPayloadLimitFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                ToolLogging = new ToolLoggingOptions
                {
                    MaxPayloadBytes = 0
                }
            });

        AssertFailure(result, "ToolLogging:MaxPayloadBytes");
    }

    [Fact]
    public void InvalidQualifiedRecommendationKeyFails()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions
            {
                RecommendedVersions = new Dictionary<string, string>
                {
                    ["nuget:bad environment/Company.Customer"] = "4.2.0"
                }
            });

        AssertFailure(result, "environment-qualified library ID");
    }

    private static void AssertFailure(
        ValidateOptionsResult result,
        string expectedText)
    {
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(expectedText, StringComparison.Ordinal));
    }
}
