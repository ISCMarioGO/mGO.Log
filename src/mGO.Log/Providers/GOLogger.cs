using System.Diagnostics;
using System.Text.Json;
using mGO.Log.Abstractions.Models;
using mGO.Log.Security;
using mGO.Log.Internal;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Providers;

/// <summary>Captures category-specific log calls synchronously and queues immutable snapshots.</summary>
public sealed class GOLogger : ILogger
{
    private readonly string _category;
    private readonly GOLoggerProvider _provider;
    internal GOLogger(string category, GOLoggerProvider provider) { _category = category; _provider = provider; }
    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _provider.Scopes.Push(state);
    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        ArgumentNullException.ThrowIfNull(formatter);
        try
        {
            var options = _provider.Options;
            var snapshot = new GOLogSnapshot(options.Security);
            var values = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? properties.Take(options.Security.MaxProperties + 1).ToArray() : [];
            var structured = snapshot.Properties(values);
            var template = values.FirstOrDefault(x => x.Key == "{OriginalFormat}").Value as string;
            var scopes = new List<JsonElement>();
            GOLogOperationState? operation = null;
            var operationDepth = -1;
            _provider.Scopes.ForEachScope((scope, list) =>
            {
                if (scope is GOLogOperationState operationState)
                {
                    operation = operationState;
                    operationDepth = Math.Min(operationDepth + 1, 64);
                }
                if (options.IncludeScopes && list.Count < options.Security.MaxProperties) list.Add(snapshot.Capture(scope));
            }, scopes);
            var operationName = operation is null ? null : snapshot.CaptureProperty("OperationName", operation.Name).GetString();
            var activity = options.IncludeActivityTracking ? Activity.Current : null;
            var message = snapshot.Redact(formatter(state, exception));
            _provider.Enqueue(new GOLogEntry(DateTimeOffset.UtcNow, logLevel, _category, eventId,
                message, template is null ? null : snapshot.Redact(template), structured,
                exception?.GetType().FullName,
                exception is null ? null : snapshot.Redact(exception.Message),
                exception is null ? null : snapshot.Redact(exception.ToString()),
                activity?.TraceId.ToString(), activity?.SpanId.ToString(), scopes, options.ApplicationName,
                options.IncludeExecutionContext ? new GOLogExecutionContext(Environment.ProcessId,
                    snapshot.Redact(_provider.ProcessName), Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.Name is { } threadName ? snapshot.Redact(threadName) : null) : null,
                state is GOLogOperationEvent operationEvent ? operationEvent.Kind : GOLogEventKind.Message,
                operation is null ? null : new GOLogOperationContext(snapshot.Redact(operationName!), operationDepth, operation.ElapsedMilliseconds)));
        }
        catch (Exception ex) { _provider.Diagnostics.Error(ex, capture: true); }
    }
}
