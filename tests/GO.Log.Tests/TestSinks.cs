using System.Collections.Concurrent;
using GO.Log.Abstractions.Interfaces;
using GO.Log.Abstractions.Models;

namespace GO.Log.Tests;

internal class MemorySink : IGOLogSink
{
    public ConcurrentQueue<GOLogEntry> Entries { get; } = new();
    public ConcurrentQueue<int> Batches { get; } = new();
    public int Flushes;
    public bool Disposed;
    public virtual ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default)
    {
        Batches.Enqueue(entries.Count);
        foreach (var entry in entries) Entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }
    public virtual ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Flushes);
        return ValueTask.CompletedTask;
    }
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

internal sealed class BlockingSink : MemorySink
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        await base.WriteBatchAsync(entries, cancellationToken);
    }
}

internal sealed class FailingSink(bool flush = false) : MemorySink
{
    public override ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default) =>
        flush ? base.WriteBatchAsync(entries, cancellationToken) : throw new IOException("Write denied");
    public override ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
        flush ? throw new IOException("Flush denied") : base.FlushAsync(cancellationToken);
}
