using mGO.Log.Configuration;
using mGO.Log.Providers;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Tests;

public sealed class QueueTests
{
    [Theory]
    [InlineData(GOLogQueueFullMode.RejectNew, 1, 2)]
    [InlineData(GOLogQueueFullMode.DropOldest, 3, 4)]
    public async Task SaturationCountsEveryDiscardAndKeepsExpectedEvents(GOLogQueueFullMode mode, int first, int second)
    {
        var sink = new BlockingSink();
        await using var provider = new GOLoggerProvider(new() { Queue = new() { Capacity = 2, BatchSize = 1, FullMode = mode } }, [sink]);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("Event {Id}", 0);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 1; i <= 4; i++) logger.LogInformation("Event {Id}", i);
        var flush = provider.FlushAsync();
        Assert.False(flush.IsCompleted);
        Assert.Equal(2, provider.Diagnostics.Dropped);
        sink.Release.TrySetResult();
        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 0, first, second }, sink.Entries.Select(e => e.StructuredProperties["Id"].GetInt32()));
        Assert.Equal(3, provider.Diagnostics.Processed);
        Assert.True(sink.Flushes > 0);
    }
    [Theory]
    [InlineData(GOLogQueueFullMode.RejectNew)]
    [InlineData(GOLogQueueFullMode.DropOldest)]
    public async Task ConcurrentProducersAccountForEveryEventAndRespectBatchLimit(GOLogQueueFullMode mode)
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { Queue = new() { Capacity = 32, BatchSize = 7, FullMode = mode } }, [sink]);
        var logger = provider.CreateLogger("Concurrent");
        Parallel.For(0, 2000, i => logger.LogInformation("Event {Index}", i));
        await provider.StopAsync();
        Assert.Equal(2000, provider.Diagnostics.Processed + provider.Diagnostics.Dropped + provider.Diagnostics.Failed);
        Assert.All(sink.Batches, count => Assert.InRange(count, 1, 7));
        Assert.Equal(sink.Entries.Count, sink.Entries.Select(x => x.StructuredProperties["Index"].GetInt32()).Distinct().Count());
        Assert.True(sink.Disposed);
        Assert.False(logger.IsEnabled(LogLevel.Critical));
        await provider.StopAsync();
        await provider.FlushAsync();
        logger.LogCritical("ignored");
    }
    [Fact]
    public async Task WriteFailureCountsEventsAndAllowsOtherSinksToContinue()
    {
        var good = new MemorySink();
        var bad = new FailingSink();
        await using var provider = new GOLoggerProvider(new(), [bad, good]);
        provider.CreateLogger("Test").LogError("Failure");
        await provider.FlushAsync();
        Assert.Equal(1, provider.Diagnostics.Failed);
        Assert.Equal(0, provider.Diagnostics.Processed);
        Assert.Equal(1, provider.Diagnostics.SinkFailures);
        Assert.Single(good.Entries);
    }
    [Fact]
    public async Task FlushFailureIsObservable()
    {
        await using var provider = new GOLoggerProvider(new(), [new FailingSink(flush: true)]);
        provider.CreateLogger("Test").LogInformation("event");
        await Assert.ThrowsAsync<IOException>(() => provider.FlushAsync());
        Assert.True(provider.Diagnostics.SinkFailures > 0);
    }
    [Fact]
    public async Task CancellingFlushOnlyCancelsCallerWait()
    {
        var sink = new BlockingSink();
        await using var provider = new GOLoggerProvider(new(), [sink]);
        provider.CreateLogger("Test").LogInformation("event");
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var flush = provider.FlushAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        sink.Release.TrySetResult();
        await provider.FlushAsync();
        Assert.Equal(1, provider.Diagnostics.Processed);
    }
    [Fact]
    public async Task FlushDoesNotWaitForEventsAcceptedAfterItsBarrier()
    {
        var sink = new PhasedSink();
        await using var provider = new GOLoggerProvider(new() { Queue = new() { BatchSize = 1 } }, [sink]);
        var logger = provider.CreateLogger("Barrier");
        try
        {
            logger.LogInformation("before");
            await sink.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var flush = provider.FlushAsync();
            logger.LogInformation("after");
            sink.FirstRelease.TrySetResult();
            await sink.SecondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await flush.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(flush.IsCompletedSuccessfully);
            Assert.Equal(1, provider.Diagnostics.Processed);
        }
        finally { sink.FirstRelease.TrySetResult(); sink.SecondRelease.TrySetResult(); }
    }
    [Fact]
    public async Task ShutdownTimeoutCancelsCooperativeSinkAndDropsPendingEvents()
    {
        var sink = new BlockingSink();
        var provider = new GOLoggerProvider(new() { Queue = new() { ShutdownTimeoutSeconds = 1, BatchSize = 1 } }, [sink]);
        provider.CreateLogger("Test").LogInformation("in flight");
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        provider.CreateLogger("Test").LogInformation("pending");
        var flush = provider.FlushAsync();
        await provider.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
        Assert.Equal(1, provider.Diagnostics.Failed);
        Assert.Equal(1, provider.Diagnostics.Dropped);
        Assert.True(sink.Disposed);
        await provider.DisposeAsync();
    }

    private sealed class PhasedSink : MemorySink
    {
        private int _calls;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteBatchAsync(IReadOnlyList<mGO.Log.Abstractions.Models.GOLogEntry> entries, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstEntered.TrySetResult();
                await FirstRelease.Task.WaitAsync(cancellationToken);
            }
            else
            {
                SecondEntered.TrySetResult();
                await SecondRelease.Task.WaitAsync(cancellationToken);
            }
            await base.WriteBatchAsync(entries, cancellationToken);
        }
    }
}
