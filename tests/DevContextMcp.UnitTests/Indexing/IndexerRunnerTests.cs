using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class IndexerRunnerTests
{
    [Fact]
    public async Task NoConfiguredSourcesSucceedsWithoutInvokingCoordinator()
    {
        var coordinator = new UnexpectedCoordinator();
        var executor = new IndexerRunner(
            Options.Create(new IndexerOptions()),
            coordinator,
            NullLogger<IndexerRunner>.Instance);

        var succeeded = await executor.RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        Assert.False(coordinator.WasCalled);
    }

    [Theory]
    [InlineData("succeeded", true)]
    [InlineData("partial_success", false)]
    [InlineData("failed", false)]
    public async Task ResultReflectsRunStatus(string status, bool expected)
    {
        var runner = CreateRunner(new StubCoordinator(
        [
            Summary(status)
        ]));

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(expected, succeeded);
    }

    [Fact]
    public async Task ExceptionReturnsFailure()
    {
        var runner = CreateRunner(new StubCoordinator(
            exception: new InvalidOperationException("failure")));

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = CreateRunner(new StubCoordinator(
            exception: new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(cancellation.Token));
    }

    [Fact]
    public async Task SummaryListsSortedPackageChangesAndEmptySections()
    {
        var logger = new CapturingLogger();
        var summary = new IndexRunSummary(
            "fixture",
            "succeeded",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            4,
            4,
            3,
            1,
            [
                new PackageIdentityKey("Zulu.Package", "2.0.0"),
                new PackageIdentityKey("Alpha.Package", "1.0.0")
            ],
            [new PackageIdentityKey("Updated.Package", "3.0.0")],
            [],
            []);
        var runner = CreateRunner(new StubCoordinator([summary]), logger);

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("Added (2):", message);
        Assert.Contains("Alpha.Package 1.0.0", message);
        Assert.Contains("Zulu.Package 2.0.0", message);
        Assert.Contains("Updated (1):", message);
        Assert.Contains("Updated.Package 3.0.0", message);
        Assert.Contains("Deleted (0):", message);
        Assert.DoesNotContain("Changed:", message);
        Assert.DoesNotContain("Unchanged:", message);
    }

    private static IndexerRunner CreateRunner(
        IIndexCoordinator coordinator,
        ILogger<IndexerRunner>? logger = null) =>
        new(
            Options.Create(new IndexerOptions
            {
                Environments =
                [
                    new NuGetEnvironmentOptions
                    {
                        Name = "test",
                        ServiceIndex = "fixture"
                    }
                ]
            }),
            coordinator,
            logger ?? NullLogger<IndexerRunner>.Instance);

    private static IndexRunSummary Summary(string status) =>
        new(
            "fixture",
            status,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            status == "failed" ? 0 : 1,
            status == "succeeded" ? 1 : 0,
            0,
            status == "succeeded"
                ? [new PackageIdentityKey("Fixture.Package", "1.0.0")]
                : [],
            [],
            [],
            []);

    private sealed class UnexpectedCoordinator : IIndexCoordinator
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<IndexRunSummary>> IndexAllAsync(
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Coordinator should not be called.");
        }
    }

    private sealed class StubCoordinator(
        IReadOnlyList<IndexRunSummary>? summaries = null,
        Exception? exception = null) : IIndexCoordinator
    {
        public Task<IReadOnlyList<IndexRunSummary>> IndexAllAsync(
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(summaries ?? []);
        }
    }

    private sealed class CapturingLogger : ILogger<IndexerRunner>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

}
