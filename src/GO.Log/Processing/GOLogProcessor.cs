using GO.Log.Abstractions.Interfaces;
using GO.Log.Configuration;
using GO.Log.Diagnostics;
using GO.Log.Queues;

namespace GO.Log.Processing;

internal sealed class GOLogProcessor(GOLogQueue queue, IReadOnlyList<IGOLogSink> sinks,
    GOLogOptions options, GOLogDiagnostics diagnostics)
{
    public async Task RunAsync(CancellationToken token)
    {
        var lastFlush = DateTimeOffset.UtcNow;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var batch = queue.TakeBatch();
                if (batch.Count > 0)
                {
                    var failed = false;
                    var entries = batch.Select(x => x.Entry).ToArray();
                    foreach (var sink in sinks)
                    {
                        try { await sink.WriteBatchAsync(entries, token).ConfigureAwait(false); }
                        catch (Exception ex) { diagnostics.Error(ex); failed = true; }
                    }
                    if (failed) diagnostics.Fail(batch.Count); else diagnostics.Process(batch.Count);
                    queue.Settle(batch);
                }
                var ready = queue.ReadyFlushes();
                if (ready.Count > 0 || queue.IsComplete ||
                    DateTimeOffset.UtcNow - lastFlush >= TimeSpan.FromSeconds(options.File.FlushIntervalSeconds))
                {
                    var error = await FlushSinksAsync(token).ConfigureAwait(false);
                    foreach (var completion in ready)
                    {
                        if (error is null) completion.TrySetResult(); else completion.TrySetException(error);
                    }
                    lastFlush = DateTimeOffset.UtcNow;
                }
                if (queue.IsComplete) break;
                if (batch.Count == 0)
                    await queue.WaitAsync(TimeSpan.FromSeconds(options.File.FlushIntervalSeconds), token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { diagnostics.Error(ex); queue.Abandon(ex); }
        finally
        {
            foreach (var sink in sinks)
            {
                try { await sink.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { diagnostics.Error(ex); }
            }
        }
    }
    private async Task<Exception?> FlushSinksAsync(CancellationToken token)
    {
        Exception? error = null;
        foreach (var sink in sinks)
        {
            try { await sink.FlushAsync(token).ConfigureAwait(false); }
            catch (Exception ex) { diagnostics.Error(ex); error = ex; }
        }
        return error;
    }
}
