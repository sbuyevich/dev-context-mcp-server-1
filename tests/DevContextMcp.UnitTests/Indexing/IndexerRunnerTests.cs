using DevContextMcp.Indexer;
using DevContextMcp.Indexer.Configuration;
using DevContextMcp.Indexer.Core.Models;
using DevContextMcp.Indexer.Core.Services;
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

    private static IndexerRunner CreateRunner(IIndexCoordinator coordinator) =>
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
            NullLogger<IndexerRunner>.Instance);

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
}
