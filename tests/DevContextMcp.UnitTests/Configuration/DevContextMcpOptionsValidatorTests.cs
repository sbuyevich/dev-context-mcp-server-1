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

    [Fact]
    public void LoopbackMcpUrlWithPathIsValid()
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions { McpUrl = "http://127.0.0.1:2222/mcp" });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Theory]
    [InlineData("https://127.0.0.1:5034/mcp")]
    [InlineData("http://0.0.0.0:5034/mcp")]
    [InlineData("http://example.com:5034/mcp")]
    [InlineData("not-a-url")]
    [InlineData("http://127.0.0.1:5034")]
    [InlineData("http://127.0.0.1:5034/mcp?mode=test")]
    [InlineData("http://127.0.0.1:5034/mcp#fragment")]
    public void UnsafeMcpUrlFails(string url)
    {
        var result = _validator.Validate(
            null,
            new DevContextMcpOptions { McpUrl = url });

        AssertFailure(result, "McpUrl");
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

    private static void AssertFailure(
        ValidateOptionsResult result,
        string expectedText)
    {
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(expectedText, StringComparison.Ordinal));
    }
}
