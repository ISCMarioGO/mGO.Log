namespace GO.Log.Diagnostics;

/// <summary>Thread-safe counters; failures are reported here without recursive logging.</summary>
public sealed class GOLogDiagnostics
{
    private long _processed, _dropped, _failed, _sinkFailures, _captureFailures;
    private string? _lastError;
    /// <summary>Events successfully written to every sink.</summary>
    public long Processed => Interlocked.Read(ref _processed);
    /// <summary>Events rejected, evicted, or abandoned during shutdown.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);
    /// <summary>Events for which at least one sink failed.</summary>
    public long Failed => Interlocked.Read(ref _failed);
    /// <summary>Sink write, flush, dispose or processor errors.</summary>
    public long SinkFailures => Interlocked.Read(ref _sinkFailures);
    /// <summary>Consumer state/formatter capture failures.</summary>
    public long CaptureFailures => Interlocked.Read(ref _captureFailures);
    /// <summary>Most recent error type, deliberately excluding potentially sensitive exception text.</summary>
    public string? LastError => Volatile.Read(ref _lastError);
    internal void Process(int count) => Interlocked.Add(ref _processed, count);
    internal void Drop(int count = 1) => Interlocked.Add(ref _dropped, count);
    internal void Fail(int count) => Interlocked.Add(ref _failed, count);
    internal void Error(Exception error, bool capture = false)
    {
        if (capture) Interlocked.Increment(ref _captureFailures);
        else Interlocked.Increment(ref _sinkFailures);
        Volatile.Write(ref _lastError, error.GetType().FullName);
    }
}
