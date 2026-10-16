using GO.Log.Abstractions.Models;
using GO.Log.Providers;
using Microsoft.Extensions.Logging;

namespace GO.Log.Tests;

public sealed class OperationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NestedOperationsFlowAcrossAwaitsAndCaptureTiming(bool includeScopes)
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Debug, IncludeScopes = includeScopes }, [sink]);
        var logger = provider.CreateLogger("Orders");
        using (logger.BeginGOLogOperation("Outer"))
        {
            await Task.Yield();
            using (logger.BeginGOLogOperation("Inner"))
            {
                await Task.Yield();
                logger.LogInformation("work");
            }
            logger.LogWarning("after inner");
        }
        logger.LogInformation("outside");
        await provider.FlushAsync();
        var events = sink.Entries.ToArray();
        Assert.Equal(7, events.Length);
        Assert.Equal(new[] { GOLogEventKind.Enter, GOLogEventKind.Enter, GOLogEventKind.Message,
            GOLogEventKind.Leave, GOLogEventKind.Message, GOLogEventKind.Leave, GOLogEventKind.Message }, events.Select(e => e.EventKind));
        Assert.Equal(new[] { 0, 1, 1, 1, 0, 0 }, events.Take(6).Select(e => e.Operation!.Depth));
        Assert.All(events.Take(6), e => Assert.True(e.Operation!.ElapsedMilliseconds >= 0));
        Assert.True(events[5].Operation!.ElapsedMilliseconds >= events[0].Operation!.ElapsedMilliseconds);
        Assert.Null(events[6].Operation);
        Assert.Equal("Inner", events[2].Operation!.Name);
        Assert.Equal("Outer", events[4].Operation!.Name);
        if (!includeScopes) Assert.All(events, e => Assert.Empty(e.ScopeProperties));
    }
    [Fact]
    public async Task ConcurrentOperationsDoNotShareLogicalNesting()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Debug }, [sink]);
        var logger = provider.CreateLogger("Concurrent");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var operation = logger.BeginGOLogOperation("Operation-" + i);
            await Task.Yield();
            logger.LogInformation("item {Index}", i);
        }));
        await provider.FlushAsync();
        Assert.Equal(60, sink.Entries.Count);
        Assert.All(sink.Entries, e => Assert.Equal(0, e.Operation!.Depth));
        foreach (var entry in sink.Entries.Where(e => e.EventKind == GOLogEventKind.Message))
            Assert.Equal("Operation-" + entry.StructuredProperties["Index"].GetInt32(), entry.Operation!.Name);
    }
    [Fact]
    public async Task ExitIsIdempotentAndStillRunsOnException()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Debug }, [sink]);
        var logger = provider.CreateLogger("Exceptions");
        var operation = logger.BeginGOLogOperation("Work");
        try { using (operation) throw new InvalidOperationException("Expected"); }
        catch (InvalidOperationException) { }
        operation.Dispose();
        logger.LogInformation("after");
        await provider.FlushAsync();
        Assert.Equal(3, sink.Entries.Count);
        Assert.Single(sink.Entries, e => e.EventKind == GOLogEventKind.Leave);
        Assert.Null(sink.Entries.Last().Operation);
    }
    [Fact]
    public async Task ProducerThreadIsCapturedBeforeAsynchronousProcessing()
    {
        var sink = new BlockingSink();
        await using var provider = new GOLoggerProvider(new(), [sink]);
        var logger = provider.CreateLogger("Producer");
        var producerId = Environment.CurrentManagedThreadId;
        logger.LogInformation("origin");
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sink.Release.TrySetResult();
        await provider.FlushAsync();
        var context = Assert.Single(sink.Entries).ExecutionContext!;
        Assert.Equal(producerId, context.ThreadId);
        Assert.Equal(Environment.ProcessId, context.ProcessId);
        Assert.NotEmpty(context.ProcessName);
    }
    [Fact]
    public async Task ExecutionContextCanBeDisabledAndOperationNamesRespectMaskingWithoutScopes()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new()
        {
            MinimumLevel = LogLevel.Debug, IncludeExecutionContext = false, IncludeScopes = false,
            Security = new() { SensitiveKeys = ["OperationName"] }
        }, [sink]);
        using (provider.CreateLogger("Test").BeginGOLogOperation("sensitive operation")) { }
        await provider.FlushAsync();
        Assert.All(sink.Entries, entry =>
        {
            Assert.Null(entry.ExecutionContext);
            Assert.Equal("***", entry.Operation!.Name);
            Assert.DoesNotContain("sensitive operation", entry.Message);
        });
    }
    [Fact]
    public void OperationArgumentsAreValidated()
    {
        using var provider = new GOLoggerProvider(new(), []);
        var logger = provider.CreateLogger("Args");
        Assert.Throws<ArgumentNullException>(() => GOLogOperationExtensions.BeginGOLogOperation(null!, "op"));
        Assert.Throws<ArgumentException>(() => logger.BeginGOLogOperation(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.BeginGOLogOperation("op", LogLevel.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => logger.BeginGOLogOperation("op", (LogLevel)99));
    }
}
