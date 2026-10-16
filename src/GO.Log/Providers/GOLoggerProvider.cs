using System.Diagnostics;
using GO.Log.Abstractions.Interfaces;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using GO.Log.Diagnostics;
using GO.Log.Internal;
using GO.Log.Processing;
using GO.Log.Queues;
using GO.Log.Sinks;
using Microsoft.Extensions.Logging;

namespace GO.Log.Providers;

/// <summary>Owns one queue, supervised consumer and destinations. Dispose asynchronously for bounded shutdown.</summary>
[ProviderAlias("GOLog")]
public sealed class GOLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    private readonly GOLogQueue _queue;
    private readonly CancellationTokenSource _abort = new();
    private readonly Task _worker;
    private readonly object _lifecycle = new();
    private Task? _stop;
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    private int _stopped;
    internal GOLogOptions Options { get; }
    internal string ProcessName { get; }
    internal IExternalScopeProvider Scopes => Volatile.Read(ref _scopes);
    /// <summary>Thread-safe counters and error diagnostics.</summary>
    public GOLogDiagnostics Diagnostics { get; } = new();

    /// <summary>Starts the consumer. Null sinks selects configured file/console destinations; explicit sinks replace them.</summary>
    public GOLoggerProvider(GOLogOptions options, IEnumerable<IGOLogSink>? sinks = null)
    {
        Options = OptionsSnapshot.Copy(options);
        using (var process = Process.GetCurrentProcess()) ProcessName = process.ProcessName;
        var destinations = sinks?.ToArray() ?? CreateDefaultSinks();
        if (destinations.Any(s => s is null)) throw new ArgumentException("Sinks cannot contain null.", nameof(sinks));
        _queue = new GOLogQueue(Options.Queue, Diagnostics);
        var processor = new GOLogProcessor(_queue, destinations, Options, Diagnostics);
        _worker = Task.Run(() => processor.RunAsync(_abort.Token));
    }
    private IGOLogSink[] CreateDefaultSinks()
    {
        var sinks = new List<IGOLogSink>();
        if (Options.File.Enabled) sinks.Add(new GOFileSink(Options));
        if (Options.Console.Enabled) sinks.Add(new GOConsoleSink(Options.Console));
        return sinks.ToArray();
    }
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new GOLogger(categoryName, this);
    }
    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        Volatile.Write(ref _scopes, scopeProvider);
    }
    internal bool IsEnabled(LogLevel level) => Volatile.Read(ref _stopped) == 0 && Options.Enabled &&
        level >= Options.MinimumLevel && level < LogLevel.None && level >= LogLevel.Trace;
    internal void Enqueue(GOLogEntry entry) => _queue.Enqueue(entry);

    /// <summary>
    /// Waits until events accepted before this call have been written, failed, or dropped, then flushes sinks.
    /// Concurrent later events need not finish. Cancellation cancels only this wait. Write failures are counted;
    /// a sink flush failure faults this task. No physical disk durability is promised.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_lifecycle) task = _stop ?? _queue.RequestFlush();
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Stops capture, drains for the configured grace period, then requests sink cancellation.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycle) return (_stop ??= StopCoreAsync()).WaitAsync(cancellationToken);
    }
    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _queue.Complete();
        _abort.CancelAfter(TimeSpan.FromSeconds(Options.Queue.ShutdownTimeoutSeconds));
        await _worker.ConfigureAwait(false);
        _abort.Dispose();
    }
    /// <summary>Synchronously drains and disposes; hosts should use StopAsync before synchronous disposal.</summary>
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
