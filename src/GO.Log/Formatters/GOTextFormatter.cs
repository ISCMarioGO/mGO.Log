using System.Globalization;
using System.Text;
using System.Text.Json;
using GO.Log.Abstractions.Interfaces;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using Microsoft.Extensions.Logging;

namespace GO.Log.Formatters;

/// <summary>Invariant-culture text layouts with escaped control characters and optional structured context.</summary>
public sealed class GOTextFormatter : IGOLogFormatter
{
    private readonly GOLogTextOptions _options;
    /// <summary>Creates a formatter with an independently captured and validated layout.</summary>
    public GOTextFormatter(GOLogTextOptions? options = null)
    {
        options ??= new();
        GOLogOptions.ValidateText(options);
        _options = new()
        {
            Style = options.Style, TimestampFormat = options.TimestampFormat, IncludeProperties = options.IncludeProperties,
            IncludeScopes = options.IncludeScopes, IncludeExceptionDetails = options.IncludeExceptionDetails
        };
    }
    /// <inheritdoc />
    public string Format(GOLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var timestamp = entry.Timestamp.ToString(_options.TimestampFormat, CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        switch (_options.Style)
        {
            case GOLogTextStyle.Compact:
                text.Append(timestamp).Append(" [").Append(ShortLevel(entry.LogLevel)).Append("] ")
                    .Append(Escape(entry.Category)).Append(": ").Append(Escape(entry.Message));
                break;
            case GOLogTextStyle.Trace:
                var context = entry.ExecutionContext;
                var producer = context is null ? entry.ApplicationName ?? "" :
                    FormattableString.Invariant($"{context.ProcessName}/{context.ProcessId}:{context.ThreadName ?? "thread"}/{context.ThreadId}");
                text.Append(Marker(entry)).Append(' ').Append(timestamp).Append(" [")
                    .Append(Column(producer, 42)).Append("] [").Append(entry.LogLevel.ToString().PadRight(11))
                    .Append("] [elapsed=").Append(Duration(entry.Operation?.ElapsedMilliseconds)).Append("] ")
                    .Append(Column(entry.Category, 30)).Append(" |")
                    .Append(' ', entry.Operation?.Depth ?? 0).Append(Marker(entry)).Append(' ').Append(Escape(entry.Message));
                break;
            default:
                text.Append(timestamp).Append(" [").Append(entry.LogLevel).Append("] ").Append(Escape(entry.ApplicationName ?? ""))
                    .Append(' ').Append(Escape(entry.Category)).Append(" (").Append(entry.EventId.Id.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(Escape(entry.EventId.Name ?? "")).Append(") ").Append(Escape(entry.Message));
                if (entry.MessageTemplate is not null) text.Append(" Template=").Append(Escape(entry.MessageTemplate));
                break;
        }
        if (_options.IncludeProperties && entry.StructuredProperties.Count > 0)
            text.Append(" Properties=").Append(JsonSerializer.Serialize(entry.StructuredProperties));
        if (_options.IncludeScopes && !entry.ScopeProperties.IsEmpty)
            text.Append(" Scopes=").Append(JsonSerializer.Serialize(entry.ScopeProperties));
        if (entry.TraceId is not null) text.Append(" TraceId=").Append(Escape(entry.TraceId)).Append(" SpanId=").Append(Escape(entry.SpanId ?? ""));
        if (entry.ExceptionDetails is not null || entry.ExceptionType is not null)
            text.Append(" Exception=").Append(Escape(_options.IncludeExceptionDetails
                ? entry.ExceptionDetails ?? entry.ExceptionMessage ?? ""
                : $"{entry.ExceptionType}: {entry.ExceptionMessage}"));
        return text.ToString();
    }
    private static string Duration(double? milliseconds) => milliseconds switch
    {
        null => "          -",
        < 1 => (milliseconds.Value * 1000).ToString("F2", CultureInfo.InvariantCulture).PadLeft(8) + " us",
        < 1000 => milliseconds.Value.ToString("F2", CultureInfo.InvariantCulture).PadLeft(8) + " ms",
        _ => (milliseconds.Value / 1000).ToString("F2", CultureInfo.InvariantCulture).PadLeft(8) + " s "
    };
    private static char Marker(GOLogEntry entry) => entry.EventKind switch
    {
        GOLogEventKind.Enter => '>', GOLogEventKind.Leave => '<',
        _ => entry.LogLevel switch { LogLevel.Warning => '!', LogLevel.Error or LogLevel.Critical => '@', _ => '-' }
    };
    private static string ShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF",
        LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "NON"
    };
    private static string Column(string value, int width)
    {
        value = Escape(value);
        return value.Length > width ? value[..(width - 1)] + "~" : value.PadRight(width);
    }
    private static string Escape(string value)
    {
        if (!value.Any(NeedsEscape)) return value;
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!NeedsEscape(c)) { escaped.Append(c); continue; }
            escaped.Append(c switch
            {
                '\r' => "\\r", '\n' => "\\n", '\t' => "\\t",
                _ => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
            });
        }
        return escaped.ToString();
    }
    private static bool NeedsEscape(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';
}
