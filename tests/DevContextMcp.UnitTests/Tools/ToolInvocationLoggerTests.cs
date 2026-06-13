using System.Text;
using System.Text.Json;
using DevContextMcp.Server.Configuration;
using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DevContextMcp.UnitTests.Tools;

public sealed class ToolInvocationLoggerTests
{
    [Fact]
    public async Task InvokeAsync_DebugEnabled_LogsCompleteRequestAndResponse()
    {
        var logger = new CapturingLogger(LogLevel.Debug);
        var target = CreateTarget(logger);
        var request = new ListVersionsRequest("nuget:qa/Demo.Cities", true);
        var response = Response();

        var actual = await target.InvokeAsync(
            "list_versions",
            request,
            _ => Task.FromResult(response),
            CancellationToken.None);

        Assert.Same(response, actual);
        var (requestLog, responseLog) = AssertPairedLogs(logger);
        Assert.Equal("request", requestLog.Property<string>("Direction"));
        Assert.Equal("response", responseLog.Property<string>("Direction"));
        Assert.False(requestLog.Property<bool>("PayloadTruncated"));
        Assert.False(responseLog.Property<bool>("PayloadTruncated"));
        Assert.Contains(
            "\"libraryId\":\"nuget:qa/Demo.Cities\"",
            requestLog.Property<string>("Payload"),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"status\":\"ok\"",
            responseLog.Property<string>("Payload"),
            StringComparison.Ordinal);
        Assert.True(responseLog.Property<double>("ElapsedMilliseconds") >= 0);
    }

    [Fact]
    public async Task InvokeAsync_OversizedPayload_LogsBoundedJsonEnvelope()
    {
        const int maximumBytes = 256;
        var logger = new CapturingLogger(LogLevel.Debug);
        var target = CreateTarget(logger, maximumBytes);
        var request = new { Query = new string('x', 2_000) };

        await target.InvokeAsync(
            "resolve_library",
            request,
            _ => Task.FromResult(new { Status = "ok" }),
            CancellationToken.None);

        var requestLog = AssertPairedLogs(logger).Request;
        Assert.True(requestLog.Property<bool>("PayloadTruncated"));
        Assert.True(requestLog.Property<int>("PayloadBytes") > maximumBytes);
        var payload = requestLog.Property<string>("Payload");
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= maximumBytes);
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal(
            requestLog.Property<int>("PayloadBytes"),
            document.RootElement.GetProperty("originalUtf8Bytes").GetInt32());
        Assert.NotEmpty(document.RootElement.GetProperty("preview").GetString()!);
    }

    [Fact]
    public async Task InvokeAsync_DebugDisabled_DoesNotSerializePayload()
    {
        var logger = new CapturingLogger(LogLevel.Information);
        var target = CreateTarget(logger);
        var request = new ThrowingPayload();

        var actual = await target.InvokeAsync(
            "fixture",
            request,
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", actual);
        Assert.Equal(0, request.GetterCalls);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task InvokeAsync_RequestSerializationFails_ReturnsToolResponse()
    {
        var logger = new CapturingLogger(LogLevel.Debug);
        var target = CreateTarget(logger);

        var actual = await target.InvokeAsync(
            "fixture",
            new ThrowingPayload(),
            _ => Task.FromResult("response"),
            CancellationToken.None);

        Assert.Equal("response", actual);
        Assert.Contains(
            logger.Entries,
            entry => entry.Property<string>("Direction") == "response");
    }

    [Fact]
    public async Task InvokeAsync_Exception_LogsErrorAndRethrows()
    {
        var logger = new CapturingLogger(LogLevel.Debug);
        var target = CreateTarget(logger);
        var expected = new InvalidOperationException("failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            target.InvokeAsync<string, string>(
                "fixture",
                "request",
                _ => Task.FromException<string>(expected),
                CancellationToken.None));

        Assert.Same(expected, actual);
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Same(expected, error.Exception);
        Assert.Equal("fixture", error.Property<string>("ToolName"));
        Assert.True(error.Property<double>("ElapsedMilliseconds") >= 0);
    }

    [Fact]
    public async Task InvokeAsync_Cancellation_LogsDebugAndRethrows()
    {
        var logger = new CapturingLogger(LogLevel.Debug);
        var target = CreateTarget(logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            target.InvokeAsync<string, string>(
                "fixture",
                "request",
                _ => Task.FromCanceled<string>(cancellation.Token),
                cancellation.Token));

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("was canceled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_LoggingFails_DoesNotChangeToolBehavior()
    {
        var target = new ToolInvocationLogger(
            Options.Create(new DevContextMcpOptions()),
            new ThrowingLogger());

        var actual = await target.InvokeAsync(
            "fixture",
            "request",
            _ => Task.FromResult("response"),
            CancellationToken.None);

        Assert.Equal("response", actual);
    }

    [Fact]
    public async Task ResolveLibraryTool_LogsNormalizedRequestAndResponse()
    {
        var handler = new Mock<IResolveLibraryHandler>();
        var response = new ResolveLibraryResponse
        {
            Status = ToolResultStatus.Ok,
            Data = new ResolveLibraryResult()
        };
        handler.Setup(value => value.HandleAsync(
                It.IsAny<ResolveLibraryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var logger = new CapturingLogger(LogLevel.Debug);
        var tool = new ResolveLibraryTool(handler.Object, CreateTarget(logger));

        await tool.ResolveLibraryAsync(
            """{"query":"wrapped","includePrerelease":true,"limit":3,"environment":"qa"}""");

        handler.Verify(value => value.HandleAsync(
            new ResolveLibraryRequest("wrapped", true, 3, "qa"),
            It.IsAny<CancellationToken>()), Times.Once);
        AssertToolLogs(logger, "resolve_library", "\"query\":\"wrapped\"");
    }

    [Fact]
    public async Task QueryDocsTool_LogsRequestAndResponse()
    {
        var handler = new Mock<IQueryDocsHandler>();
        handler.Setup(value => value.HandleAsync(
                It.IsAny<QueryDocsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryDocsResponse
            {
                Status = ToolResultStatus.Ok,
                Data = new QueryDocsResult()
            });
        var logger = new CapturingLogger(LogLevel.Debug);
        var tool = new QueryDocsTool(handler.Object, CreateTarget(logger));

        await tool.QueryDocsAsync("docs:company-docs", "testing guidance");

        AssertToolLogs(
            logger,
            "query_docs",
            "\"question\":\"testing guidance\"");
    }

    [Fact]
    public async Task GetSymbolTool_LogsRequestAndResponse()
    {
        var handler = new Mock<IGetSymbolHandler>();
        handler.Setup(value => value.HandleAsync(
                It.IsAny<GetSymbolRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSymbolResponse
            {
                Status = ToolResultStatus.NotFound,
                Data = new GetSymbolResult()
            });
        var logger = new CapturingLogger(LogLevel.Debug);
        var tool = new GetSymbolTool(handler.Object, CreateTarget(logger));

        await tool.GetSymbolAsync("nuget:qa/Demo.Cities", "CityClient");

        AssertToolLogs(logger, "get_symbol", "\"symbol\":\"CityClient\"");
    }

    [Fact]
    public async Task ListVersionsTool_LogsRequestAndResponse()
    {
        var handler = new Mock<IListVersionsHandler>();
        handler.Setup(value => value.HandleAsync(
                It.IsAny<ListVersionsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response());
        var logger = new CapturingLogger(LogLevel.Debug);
        var tool = new ListVersionsTool(handler.Object, CreateTarget(logger));

        await tool.ListVersionsAsync("nuget:prod/Demo.Cities", true);

        AssertToolLogs(
            logger,
            "list_versions",
            "\"includePrerelease\":true");
    }

    private static ToolInvocationLogger CreateTarget(
        ILogger<ToolInvocationLogger> logger,
        int maximumPayloadBytes = 32 * 1024) =>
        new(
            Options.Create(new DevContextMcpOptions
            {
                ToolLogging = new ToolLoggingOptions
                {
                    MaxPayloadBytes = maximumPayloadBytes
                }
            }),
            logger);

    private static ListVersionsResponse Response() =>
        new()
        {
            Status = ToolResultStatus.Ok,
            Data = new ListVersionsResult()
        };

    private static void AssertToolLogs(
        CapturingLogger logger,
        string toolName,
        string expectedRequestJson)
    {
        var (request, response) = AssertPairedLogs(logger);
        Assert.Equal(toolName, request.Property<string>("ToolName"));
        Assert.Equal(toolName, response.Property<string>("ToolName"));
        Assert.Contains(
            expectedRequestJson,
            request.Property<string>("Payload"),
            StringComparison.Ordinal);
    }

    private static (LogEntry Request, LogEntry Response) AssertPairedLogs(
        CapturingLogger logger)
    {
        var request = Assert.Single(
            logger.Entries,
            entry => entry.Property<string>("Direction") == "request");
        var response = Assert.Single(
            logger.Entries,
            entry => entry.Property<string>("Direction") == "response");
        Assert.Equal(
            request.Property<string>("InvocationId"),
            response.Property<string>("InvocationId"));
        return (request, response);
    }

    private sealed class ThrowingPayload
    {
        public int GetterCalls { get; private set; }

        public string Value
        {
            get
            {
                GetterCalls++;
                throw new InvalidOperationException("must not serialize");
            }
        }
    }

    private sealed class CapturingLogger(LogLevel minimumLevel) :
        ILogger<ToolInvocationLogger>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new(
                logLevel,
                formatter(state, exception),
                exception,
                properties));
        }
    }

    private sealed class ThrowingLogger : ILogger<ToolInvocationLogger>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("logging failed");
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties)
    {
        public T Property<T>(string name) => (T)Properties[name]!;
    }
}
