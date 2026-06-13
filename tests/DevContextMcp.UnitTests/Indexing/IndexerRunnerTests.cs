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
        var message = Assert.Single(
            logger.Messages,
            item => item.Contains("Environment: fixture", StringComparison.Ordinal));
        Assert.Contains("Added (2):", message);
        Assert.Contains("Alpha.Package 1.0.0", message);
        Assert.Contains("Zulu.Package 2.0.0", message);
        Assert.Contains("Updated (1):", message);
        Assert.Contains("Updated.Package 3.0.0", message);
        Assert.Contains("Deleted (0):", message);
        Assert.DoesNotContain("Changed:", message);
        Assert.DoesNotContain("Unchanged:", message);
    }

    [Fact]
    public async Task UnchangedSummaryIsNotPrinted()
    {
        var logger = new CapturingLogger();
        var summary = new IndexRunSummary(
            "fixture",
            "succeeded",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            1,
            0,
            1,
            [],
            [],
            [],
            []);
        var runner = CreateRunner(new StubCoordinator([summary]), logger);

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Environment: fixture", StringComparison.Ordinal));
        Assert.Contains(
            logger.Messages,
            message => message.Contains("Indexed libraries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InventoryIsPrintedAfterSummaries()
    {
        var logger = new CapturingLogger();
        var failedSummary = Summary("failed") with
        {
            Errors = [new IndexRunError("fixture_error", "Fixture failure.")]
        };
        var runner = CreateRunner(
            new StubCoordinator(
                [failedSummary],
                [
                    new IndexedLibrary(
                        "Demo.Cities",
                        [
                            new IndexedLibraryEnvironment("prod", ["1.0.1", "1.0.0"]),
                            new IndexedLibraryEnvironment("qa", ["1.1.0"])
                        ])
                ]),
            logger);

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.False(succeeded);
        var summaryIndex = logger.Messages.FindIndex(message =>
            message.Contains("Environment: fixture", StringComparison.Ordinal));
        var inventoryIndex = logger.Messages.FindIndex(message =>
            message.Contains("Indexed libraries", StringComparison.Ordinal));
        Assert.True(inventoryIndex > summaryIndex);

        var inventory = logger.Messages[inventoryIndex];
        Assert.Contains("Demo.Cities versions (3)", inventory);
        Assert.Contains("    prod (2): 1.0.1, 1.0.0", inventory);
        Assert.Contains("    qa (1): 1.1.0", inventory);
    }

    [Fact]
    public async Task IndexedDocumentsArePrintedInSortedOrder()
    {
        var logger = new CapturingLogger();
        var runner = CreateRunner(
            new StubCoordinator(
                indexedDocuments:
                [
                    "z/reference.md",
                    "API.md",
                    "a/design.md"
                ]),
            logger);

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        var report = Assert.Single(
            logger.Messages,
            message => message.Contains(
                "Indexed documents (3)",
                StringComparison.Ordinal));
        var apiIndex = report.IndexOf("API.md", StringComparison.Ordinal);
        var designIndex = report.IndexOf("a/design.md", StringComparison.Ordinal);
        var referenceIndex = report.IndexOf("z/reference.md", StringComparison.Ordinal);
        Assert.True(designIndex < apiIndex);
        Assert.True(apiIndex < referenceIndex);
    }

    [Fact]
    public async Task EmptyInventoryPrintsNone()
    {
        var logger = new CapturingLogger();
        var runner = CreateRunner(new StubCoordinator([]), logger);

        var succeeded = await runner.RunAsync(CancellationToken.None);

        Assert.True(succeeded);
        var inventory = Assert.Single(
            logger.Messages,
            message => message.Contains("Indexed libraries", StringComparison.Ordinal));
        Assert.Contains("(none)", inventory);
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

        public Task<IndexRunResult> IndexAllAsync(
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Coordinator should not be called.");
        }
    }

    private sealed class StubCoordinator(
        IReadOnlyList<IndexRunSummary>? summaries = null,
        IReadOnlyList<IndexedLibrary>? indexedLibraries = null,
        IReadOnlyList<string>? indexedDocuments = null,
        Exception? exception = null) : IIndexCoordinator
    {
        public Task<IndexRunResult> IndexAllAsync(
            CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new IndexRunResult(
                summaries ?? [],
                indexedLibraries ?? [],
                indexedDocuments ?? []));
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
