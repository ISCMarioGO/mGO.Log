using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GO.Log.Abstractions.Models;

/// <summary>An immutable snapshot of a log call, independent of consumer-owned state.</summary>
public sealed class GOLogEntry
{
    /// <summary>Creates an event and clones all JSON values into independently owned snapshots.</summary>
    public GOLogEntry(DateTimeOffset timestamp, LogLevel logLevel, string category, EventId eventId,
        string message, string? messageTemplate = null,
        IReadOnlyDictionary<string, JsonElement>? structuredProperties = null,
        string? exceptionType = null, string? exceptionMessage = null, string? exceptionDetails = null,
        string? traceId = null, string? spanId = null, IEnumerable<JsonElement>? scopeProperties = null,
        string? applicationName = null, GOLogExecutionContext? executionContext = null,
        GOLogEventKind eventKind = GOLogEventKind.Message, GOLogOperationContext? operation = null)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(message);
        Timestamp = timestamp.ToUniversalTime(); LogLevel = logLevel; Category = category; EventId = eventId;
        Message = message; MessageTemplate = messageTemplate;
        StructuredProperties = structuredProperties?.ToImmutableDictionary(p => p.Key, p => p.Value.Clone())
            ?? ImmutableDictionary<string, JsonElement>.Empty;
        ExceptionType = exceptionType; ExceptionMessage = exceptionMessage; ExceptionDetails = exceptionDetails;
        TraceId = traceId; SpanId = spanId;
        ScopeProperties = scopeProperties?.Select(x => x.Clone()).ToImmutableArray() ?? [];
        ApplicationName = applicationName;
        ExecutionContext = executionContext; EventKind = eventKind; Operation = operation;
    }
    /// <summary>UTC event timestamp.</summary>
    public DateTimeOffset Timestamp { get; }
    /// <summary>Severity.</summary>
    public LogLevel LogLevel { get; }
    /// <summary>Logger category.</summary>
    public string Category { get; }
    /// <summary>Event identifier and optional name.</summary>
    public EventId EventId { get; }
    /// <summary>Rendered, length-limited message.</summary>
    public string Message { get; }
    /// <summary>Original message template, when present.</summary>
    public string? MessageTemplate { get; }
    /// <summary>Copied structured properties.</summary>
    public ImmutableDictionary<string, JsonElement> StructuredProperties { get; }
    /// <summary>Full exception type name.</summary>
    public string? ExceptionType { get; }
    /// <summary>Exception message.</summary>
    public string? ExceptionMessage { get; }
    /// <summary>Exception text including stack and inner exceptions.</summary>
    public string? ExceptionDetails { get; }
    /// <summary>W3C trace identifier.</summary>
    public string? TraceId { get; }
    /// <summary>W3C span identifier.</summary>
    public string? SpanId { get; }
    /// <summary>Scopes in outermost-to-innermost order.</summary>
    public ImmutableArray<JsonElement> ScopeProperties { get; }
    /// <summary>Configured application name.</summary>
    public string? ApplicationName { get; }
    /// <summary>Producer process/thread snapshot, when enabled.</summary>
    public GOLogExecutionContext? ExecutionContext { get; }
    /// <summary>Message, operation entry, or operation exit; independent of severity.</summary>
    public GOLogEventKind EventKind { get; }
    /// <summary>Innermost logical operation snapshot, when present.</summary>
    public GOLogOperationContext? Operation { get; }
}
