using System.Threading.Channels;
using mGO.Log.Abstractions.Models;
using mGO.Log.Configuration;
using mGO.Log.Diagnostics;
using mGO.Log.Models;

namespace mGO.Log.Queues;

// All reads/writes and barrier bookkeeping use Gate, including manual overflow eviction.
internal sealed class GOLogQueue(GOLogQueueOptions options, GOLogDiagnostics diagnostics)
{
    private readonly Channel<QueuedLogEntry> _channel = Channel.CreateBounded<QueuedLogEntry>(
        new BoundedChannelOptions(options.Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(1);
    private readonly SortedSet<long> _pending = [];
    private readonly List<(long Target, TaskCompletionSource Completion)> _flushes = [];
    private long _sequence;
    private bool _closed;
    internal object Gate { get; } = new();
    public bool IsComplete { get { lock (Gate) return _closed && _pending.Count == 0; } }
    public bool Enqueue(GOLogEntry entry)
    {
        lock (Gate)
        {
            if (_closed) { diagnostics.Drop(); return false; }
            if (_channel.Reader.Count >= options.Capacity)
            {
                if (options.FullMode == GOLogQueueFullMode.RejectNew) { diagnostics.Drop(); return false; }
                if (_channel.Reader.TryRead(out var old)) { _pending.Remove(old.Sequence); diagnostics.Drop(); }
            }
            var item = new QueuedLogEntry(++_sequence, entry);
            _channel.Writer.TryWrite(item);
            _pending.Add(item.Sequence);
            Signal();
            return true;
        }
    }
    public List<QueuedLogEntry> TakeBatch()
    {
        lock (Gate)
        {
            var batch = new List<QueuedLogEntry>(options.BatchSize);
            while (batch.Count < options.BatchSize && _channel.Reader.TryRead(out var entry)) batch.Add(entry);
            if (_channel.Reader.Count > 0) Signal();
            return batch;
        }
    }
    public void Settle(IEnumerable<QueuedLogEntry> entries)
    {
        lock (Gate) foreach (var entry in entries) _pending.Remove(entry.Sequence);
    }
    public Task RequestFlush()
    {
        lock (Gate)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _flushes.Add((_sequence, completion)); Signal();
            return completion.Task;
        }
    }
    public List<TaskCompletionSource> ReadyFlushes()
    {
        lock (Gate)
        {
            var ready = _flushes.Where(f => _pending.Count == 0 || _pending.Min > f.Target).ToArray();
            foreach (var item in ready) _flushes.Remove(item);
            return ready.Select(x => x.Completion).ToList();
        }
    }
    public void Complete() { lock (Gate) { _closed = true; _channel.Writer.TryComplete(); Signal(); } }
    public void Abandon(Exception error)
    {
        lock (Gate)
        {
            _closed = true;
            diagnostics.Drop(_pending.Count); _pending.Clear();
            while (_channel.Reader.TryRead(out _)) { }
            foreach (var flush in _flushes) flush.Completion.TrySetException(error);
            _flushes.Clear();
        }
    }
    public async Task WaitAsync(TimeSpan interval, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(interval);
        try { await _wake.Reader.ReadAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }
    private void Signal() => _wake.Writer.TryWrite(true);
}
