using mGO.Log.Abstractions.Models;
using mGO.Log.Internal;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Diagnostics;

/// <summary>An explicit logical-operation scope that flows across awaits. Dispose once in LIFO order.</summary>
public sealed class GOLogOperation : IDisposable
{
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private readonly GOLogOperationState _state;
    private readonly IDisposable? _scope;
    private int _disposed;

    internal GOLogOperation(ILogger logger, string name, LogLevel level)
    {
        _logger = logger; _level = level; _state = new(name);
        _scope = logger.BeginScope(_state);
        try { Write(GOLogEventKind.Enter); }
        catch { _scope?.Dispose(); throw; }
    }
    /// <summary>Writes an exit event and restores the previous scope. Exit does not assert successful completion.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Write(GOLogEventKind.Leave); }
        finally { _scope?.Dispose(); }
    }
    private void Write(GOLogEventKind kind) => _logger.Log(_level,
        new EventId(0, kind == GOLogEventKind.Enter ? "GOLog.Operation.Enter" : "GOLog.Operation.Leave"),
        new GOLogOperationEvent(_state, kind), null, static (state, _) => state.Message);
}
