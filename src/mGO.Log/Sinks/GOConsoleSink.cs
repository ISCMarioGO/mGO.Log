using mGO.Log.Abstractions.Interfaces;
using mGO.Log.Abstractions.Models;
using mGO.Log.Configuration;
using mGO.Log.Formatters;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Sinks;

/// <summary>Serial-use console destination. Writes from the processor, without owning or disposing its writers.</summary>
public sealed class GOConsoleSink : IGOLogSink
{
    private readonly TextWriter _output, _error;
    private readonly GOTextFormatter _formatter;
    private readonly LogLevel _errorLevel;
    private readonly bool _colorOutput, _colorError;
    private bool _disposed;
    /// <summary>Creates an asynchronous console sink. Injected writers never receive ANSI colors and remain caller-owned.</summary>
    public GOConsoleSink(GOLogConsoleOptions options, TextWriter? output = null, TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        new GOLogOptions { Console = options }.Validate();
        _formatter = new GOTextFormatter(options.Text);
        _errorLevel = options.StandardErrorFromLevel;
        _output = output ?? Console.Out; _error = error ?? Console.Error;
        _colorOutput = options.UseColors && output is null && !Console.IsOutputRedirected;
        _colorError = options.UseColors && error is null && !Console.IsErrorRedirected;
    }
    /// <inheritdoc />
    public async ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isError = entry.LogLevel >= _errorLevel && _errorLevel != LogLevel.None;
            var writer = isError ? _error : _output;
            var line = _formatter.Format(entry);
            if (isError ? _colorError : _colorOutput) line = Color(entry.LogLevel) + line + "\u001b[0m";
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(_output, _error)) await _error.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Flushes but never disposes Console.Out, Console.Error or injected writers.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await FlushAsync().ConfigureAwait(false); }
        finally { _disposed = true; }
    }
    private static string Color(LogLevel level) => level switch
    {
        LogLevel.Warning => "\u001b[33m", LogLevel.Error => "\u001b[31m", LogLevel.Critical => "\u001b[1;31m", _ => "\u001b[37m"
    };
}
