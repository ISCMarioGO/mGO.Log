using System.Diagnostics.CodeAnalysis;
using GO.Log.Abstractions.Models;
using GO.Log.Internal;
using Microsoft.Extensions.Logging;

namespace GO.Log.Diagnostics;

/// <summary>Runtime support for compiler-instrumented methods. Prefer the Trace attributes in application code.</summary>
public sealed class GOLogMethodTrace : IDisposable
{
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private readonly GOLogOperationState _state;
    private readonly IDisposable? _scope;
    private int _completed, _disposed;
    private bool _deferred;

    /// <summary>Starts an operation using a consumer-owned logger; no service locator is used.</summary>
    public GOLogMethodTrace(ILogger logger, string operationName, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        if (!Enum.IsDefined(level) || level == LogLevel.None) throw new ArgumentOutOfRangeException(nameof(level));
        _logger = logger; _level = level; _state = new(operationName);
        _scope = logger.BeginScope(_state);
        try { Write(GOLogEventKind.Enter); }
        catch { _scope?.Dispose(); throw; }
    }
    /// <summary>Tracks a non-async Task-returning method until completion, fault or cancellation.</summary>
    [return: NotNullIfNotNull(nameof(task))]
    public Task? Track(Task? task)
    {
        if (task is null) return null;
        _deferred = true;
        return ObserveAsync(task);
    }
    /// <summary>Tracks a Task result without changing its value or swallowing its exception.</summary>
    [return: NotNullIfNotNull(nameof(task))]
    public Task<T>? Track<T>(Task<T>? task)
    {
        if (task is null) return null;
        _deferred = true;
        return ObserveAsync(task);
    }
    /// <summary>Tracks a ValueTask, consuming the original operation exactly once.</summary>
    public ValueTask Track(ValueTask task)
    {
        _deferred = true;
        return ObserveAsync(task);
    }
    /// <summary>Tracks a ValueTask result, consuming the original operation exactly once.</summary>
    public ValueTask<T> Track<T>(ValueTask<T> task)
    {
        _deferred = true;
        return ObserveAsync(task);
    }
    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        finally { Complete(); }
    }
    private async Task<T> ObserveAsync<T>(Task<T> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally { Complete(); }
    }
    private async ValueTask ObserveAsync(ValueTask task)
    {
        try { await task.ConfigureAwait(false); }
        finally { Complete(); }
    }
    private async ValueTask<T> ObserveAsync<T>(ValueTask<T> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally { Complete(); }
    }
    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0) Write(GOLogEventKind.Leave);
    }
    private void Write(GOLogEventKind kind) => _logger.Log(_level,
        new EventId(0, kind == GOLogEventKind.Enter ? "GOLog.Operation.Enter" : "GOLog.Operation.Leave"),
        new GOLogOperationEvent(_state, kind), null, static (state, _) => state.Message);
    /// <summary>Restores ambient scopes immediately; tracked tasks retain their own captured operation context.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { if (!_deferred) Complete(); }
        finally { _scope?.Dispose(); }
    }
}
