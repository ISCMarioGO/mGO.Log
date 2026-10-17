using mGO.Log.Abstractions.Models;

namespace mGO.Log.Abstractions.Interfaces;

/// <summary>A destination called serially by the processor. The engine owns and disposes its sinks.</summary>
public interface IGOLogSink : IAsyncDisposable
{
    /// <summary>Writes a batch. Throwing marks this entire batch failed; partial writes may exist.</summary>
    ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default);
    /// <summary>Flushes buffered output. This does not promise physical disk durability.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
