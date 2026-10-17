using mGO.Log.Abstractions.Models;
using mGO.Log.Providers;
using Microsoft.Extensions.Logging;
using Trace = mGO.Log.Attributes.Trace;

namespace mGO.Log.Tests;

public sealed class TraceAttributeTests
{
    [Fact]
    public async Task DecoratorInstrumentsPrivateMethodWithMultipleReturns()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("Decorators"));
        Assert.Equal(8, service.Compute(4));
        Assert.Equal(0, service.Compute(-1));
        await provider.FlushAsync();
        Assert.Equal(4, sink.Entries.Count);
        Assert.Equal(new[] { GOLogEventKind.Enter, GOLogEventKind.Leave, GOLogEventKind.Enter, GOLogEventKind.Leave }, sink.Entries.Select(e => e.EventKind));
    }
    [Fact]
    public async Task PrivateExpressionBodiesStaticParametersAndGenericMethodsAreTraced()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var logger = provider.CreateLogger("Decorators");
        var service = new TracedService(logger);
        Assert.Equal(6, service.CallPrivate(3));
        Assert.Equal("value", service.Echo("value"));
        service.VoidExpression();
        Assert.Equal(5, TracedService.StaticMethod(logger, 4));
        await provider.FlushAsync();
        Assert.Equal(8, sink.Entries.Count);
        Assert.Contains(sink.Entries, e => e.Operation!.Name == "custom private operation" && e.LogLevel == LogLevel.Information);
        Assert.Contains(sink.Entries, e => e.LogLevel == LogLevel.Critical);
        Assert.All(sink.Entries, e => Assert.Equal(0, e.Operation!.Depth));
    }
    [Fact]
    public async Task ThrownExceptionsAndCancellationStillProduceExactlyOneExit()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("Failures"));
        var expected = new InvalidOperationException("expected");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => service.Throw(expected)));
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => service.ThrowAsync(expected)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CancelAsync(new CancellationToken(true)));
        await provider.FlushAsync();
        Assert.Equal(6, sink.Entries.Count);
        Assert.Equal(3, sink.Entries.Count(e => e.EventKind == GOLogEventKind.Leave));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncAndTaskReturningMethodsWaitForCompletionWithoutLeakingCallerScope(bool asyncMethod)
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var logger = provider.CreateLogger("Tasks");
        var service = new TracedService(logger);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = asyncMethod ? service.AwaitTask(gate.Task) : service.ReturnTask(gate.Task);
        logger.LogInformation("caller after invocation");
        await provider.FlushAsync();
        Assert.Single(sink.Entries, e => e.EventKind == GOLogEventKind.Enter);
        Assert.DoesNotContain(sink.Entries, e => e.EventKind == GOLogEventKind.Leave);
        Assert.Null(Assert.Single(sink.Entries, e => e.Message == "caller after invocation").Operation);
        gate.SetResult(42);
        Assert.Equal(42, await task);
        await provider.FlushAsync();
        var exit = Assert.Single(sink.Entries, e => e.EventKind == GOLogEventKind.Leave);
        Assert.Equal(0, exit.Operation!.Depth);
        Assert.True(exit.Operation.ElapsedMilliseconds >= 0);
    }
    [Fact]
    public async Task NonAsyncTaskAndValueTaskOverloadsPreserveResultsFaultsAndCancellation()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("TaskKinds"));
        await service.ReturnPlainTask(Task.CompletedTask);
        await service.ReturnValueTask(new ValueTask(Task.CompletedTask));
        Assert.Equal(7, await service.ReturnValueTaskResult(new ValueTask<int>(7)));
        Assert.Equal(8, await service.AwaitValueTaskResult(new ValueTask<int>(8)));
        await service.AwaitPlainTask(Task.CompletedTask);
        await service.AwaitValueTask(ValueTask.CompletedTask);
        var expected = new InvalidOperationException("faulted task");
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReturnPlainTask(Task.FromException(expected))));
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReturnTask(Task.FromException<int>(expected))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReturnPlainTask(Task.FromCanceled(new CancellationToken(true))));
        Assert.Null(service.ReturnNullTask());
        Assert.Null(service.ReturnNullTaskResult());
        Assert.Null(await service.ReturnNullableResult(Task.FromResult<string?>(null)));
        await provider.FlushAsync();
        Assert.Equal(24, sink.Entries.Count);
        Assert.Equal(12, sink.Entries.Count(e => e.EventKind == GOLogEventKind.Leave));
    }
    [Fact]
    public async Task SynchronousTaskFactoryExceptionsAreNotConvertedToFaultedTasks()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("Factory"));
        var expected = new InvalidOperationException("synchronous factory failure");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => { _ = service.ThrowBeforeReturningTask(expected); }));
        await provider.FlushAsync();
        Assert.Equal(2, sink.Entries.Count);
    }
    [Fact]
    public async Task GenericAttributeAndEveryLevelRespectFiltering()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Warning }, [sink]);
        var service = new TracedService(provider.CreateLogger("Filters"));
        service.TraceLevel(); service.Compute(1); service.CallPrivate(1);
        service.WarningLevel(); service.ErrorLevel(); service.VoidExpression(); service.GenericAttribute();
        await provider.FlushAsync();
        Assert.Equal(8, sink.Entries.Count);
        Assert.All(sink.Entries, e => Assert.True(e.LogLevel >= LogLevel.Warning));
    }
    [Fact]
    public async Task NestedDecoratorsRetainOperationDepthAcrossAwait()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("Nesting"));
        Assert.Equal(12, await service.Outer(6));
        await provider.FlushAsync();
        Assert.Equal(new[] { 0, 1, 1, 0 }, sink.Entries.Select(e => e.Operation!.Depth));
    }
    [Fact]
    public async Task IteratorsTraceExecutionUntilEnumeratorDisposal()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("Iterator"));
        var enumerable = service.Iterator();
        await provider.FlushAsync();
        Assert.Empty(sink.Entries);
        using (var iterator = enumerable.GetEnumerator())
        {
            Assert.True(iterator.MoveNext());
            await provider.FlushAsync();
            Assert.Single(sink.Entries);
        }
        await provider.FlushAsync();
        Assert.Equal(2, sink.Entries.Count);
    }
    [Fact]
    public async Task TaskReturnRewritingDoesNotChangeNestedLocalFunctionsOrLambdas()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("LocalFunctions"));
        Assert.Equal(42, await service.WithLocalFunctions(Task.FromResult(42)));
        await provider.FlushAsync();
        Assert.Equal(2, sink.Entries.Count);
    }
    [Fact]
    public async Task AsyncIteratorsRemainOpenUntilEnumerationFinishes()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var service = new TracedService(provider.CreateLogger("AsyncIterator"));
        var values = new List<int>();
        await foreach (var value in service.AsyncIterator()) values.Add(value);
        await provider.FlushAsync();
        Assert.Equal(new[] { 1, 2 }, values);
        Assert.Equal(2, sink.Entries.Count);
    }
    private sealed class TracedService(ILogger logger)
    {
        private readonly ILogger _logger = logger;
        [Trace.Debug]
        public int Compute(int value)
        {
            if (value < 0) return 0;
            return value * 2;
        }
        public int CallPrivate(int value) => PrivateCompute(value);
        [Trace.Information(operationName: "custom private operation")]
        private int PrivateCompute(int value) => value * 2;
        [Trace.Debug]
        public T Echo<T>(T value) => value;
        [Trace.Critical]
        public void VoidExpression() => GC.KeepAlive(this);
        [Trace.Warning(nameof(logger))]
        public static int StaticMethod(ILogger logger, int value) => value + 1;
        [Trace.Debug]
        public int Throw(Exception error) => throw error;
        [Trace.Debug]
        public async Task<int> ThrowAsync(Exception error) { await Task.Yield(); throw error; }
        [Trace.Debug]
        public async Task CancelAsync(CancellationToken token) { await Task.Yield(); token.ThrowIfCancellationRequested(); }
        [Trace.Debug]
        public async Task<int> AwaitTask(Task<int> task) => await task;
        [Trace.Debug]
        public Task<int> ReturnTask(Task<int> task) { return task; }
        [Trace.Debug]
        public Task ReturnPlainTask(Task task) => task;
        [Trace.Debug]
        public ValueTask ReturnValueTask(ValueTask task) => task;
        [Trace.Debug]
        public ValueTask<int> ReturnValueTaskResult(ValueTask<int> task) => task;
        [Trace.Debug]
        public async ValueTask<int> AwaitValueTaskResult(ValueTask<int> task) => await task;
        [Trace.Debug]
        public async Task AwaitPlainTask(Task task) => await task;
        [Trace.Debug]
        public async ValueTask AwaitValueTask(ValueTask task) => await task;
        [Trace.Debug]
        public Task? ReturnNullTask() => null;
        [Trace.Debug]
        public Task<int>? ReturnNullTaskResult() => null;
        [Trace.Debug]
        public Task<string?> ReturnNullableResult(Task<string?> task) => task;
        [Trace.Debug]
        public Task ThrowBeforeReturningTask(Exception error) => throw error;
        [Trace.Trace]
        public void TraceLevel() { }
        [Trace.Warning]
        public void WarningLevel() { }
        [Trace.Error]
        public void ErrorLevel() { }
        [mGO.Log.Attributes.GOTrace(LogLevel.Error, operationName: "Generic attribute")]
        public void GenericAttribute() { }
        [Trace.Information]
        public async Task<int> Outer(int value) { await Task.Yield(); return Compute(value); }
        [Trace.Debug]
        public IEnumerable<int> Iterator() { yield return 1; yield return 2; }
        [Trace.Debug]
        public async IAsyncEnumerable<int> AsyncIterator() { await Task.Yield(); yield return 1; await Task.Yield(); yield return 2; }
        [Trace.Debug]
        public Task<int> WithLocalFunctions(Task<int> task)
        {
            Task<int> Local() { return task; }
            Func<Task<int>> callback = () => { return Local(); };
            return callback();
        }
    }
}
