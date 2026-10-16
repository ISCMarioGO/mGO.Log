using System.Globalization;
using System.Text.Json;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using GO.Log.Exceptions;
using GO.Log.Formatters;
using Microsoft.Extensions.Logging;

namespace GO.Log.Tests;

public sealed class TextStyleTests
{
    private static GOLogEntry Entry(LogLevel level = LogLevel.Information, GOLogEventKind kind = GOLogEventKind.Message,
        double elapsed = 12.5) => new(new DateTimeOffset(2026, 10, 16, 12, 30, 15, TimeSpan.Zero), level,
        "Orders.Service", new EventId(7, "Saved"), "Order saved", "Order {Id}",
        new Dictionary<string, JsonElement> { ["Id"] = JsonSerializer.SerializeToElement(42) },
        "InvalidOperationException", "failure", "outer\ninner", "trace", "span",
        [JsonSerializer.SerializeToElement(new { OrderId = 42 })], "Orders",
        new GOLogExecutionContext(123, "Worker", 9, "Producer"), kind, new("SaveOrder", 2, elapsed));

    [Theory]
    [InlineData(GOLogTextStyle.Standard, "[Information]")]
    [InlineData(GOLogTextStyle.Compact, "[INF]")]
    [InlineData(GOLogTextStyle.Trace, "Worker/123:Producer/9")]
    public void StylesRetainExceptionAndCorrelation(GOLogTextStyle style, string expected)
    {
        var text = new GOTextFormatter(new() { Style = style }).Format(Entry());
        Assert.Contains(expected, text);
        Assert.Contains("Order saved", text);
        Assert.Contains("Properties=", text);
        Assert.Contains("Scopes=", text);
        Assert.Contains("TraceId=trace SpanId=span", text);
        Assert.Contains("outer\\ninner", text);
        Assert.DoesNotContain('\n', text);
    }
    [Theory]
    [InlineData(LogLevel.Trace, GOLogEventKind.Message, '-')]
    [InlineData(LogLevel.Debug, GOLogEventKind.Message, '-')]
    [InlineData(LogLevel.Information, GOLogEventKind.Message, '-')]
    [InlineData(LogLevel.Warning, GOLogEventKind.Message, '!')]
    [InlineData(LogLevel.Error, GOLogEventKind.Message, '@')]
    [InlineData(LogLevel.Critical, GOLogEventKind.Message, '@')]
    [InlineData(LogLevel.Error, GOLogEventKind.Enter, '>')]
    [InlineData(LogLevel.Error, GOLogEventKind.Leave, '<')]
    public void TraceUsesReferenceDirectionAndSeverityMarkers(LogLevel level, GOLogEventKind kind, char marker)
    {
        var text = new GOTextFormatter(new() { Style = GOLogTextStyle.Trace }).Format(Entry(level, kind));
        Assert.Equal(marker, text[0]);
        Assert.Contains(" |  " + marker + " Order saved", text);
    }
    [Theory]
    [InlineData(0.5, "500.00 us")]
    [InlineData(12.5, "12.50 ms")]
    [InlineData(2500.0, "2.50 s")]
    public void TraceTimingUsesMonotonicUnitsAndInvariantCulture(double elapsed, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var text = new GOTextFormatter(new() { Style = GOLogTextStyle.Trace, TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff" }).Format(Entry(elapsed: elapsed));
            Assert.Contains("2026-10-16 12:30:15.000", text);
            Assert.Contains(expected, text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void CompactUsesShortSeverityNames(LogLevel level, string expected) =>
        Assert.Contains("[" + expected + "]", new GOTextFormatter(new() { Style = GOLogTextStyle.Compact }).Format(Entry(level)));

    [Fact]
    public void TextOptionsAreCopiedAndCanHideVerboseContext()
    {
        var options = new GOLogTextOptions { Style = GOLogTextStyle.Trace, IncludeProperties = false, IncludeScopes = false, IncludeExceptionDetails = false };
        var formatter = new GOTextFormatter(options);
        options.Style = GOLogTextStyle.Standard;
        options.IncludeProperties = true;
        var text = formatter.Format(Entry());
        Assert.StartsWith("- ", text);
        Assert.DoesNotContain("Properties=", text);
        Assert.DoesNotContain("Scopes=", text);
        Assert.DoesNotContain("inner", text);
        Assert.Contains("Exception=InvalidOperationException: failure", text);
    }
    [Fact]
    public void TraceHandlesMissingContextAndClipsColumnsWithoutEmittingControls()
    {
        var formatter = new GOTextFormatter(new() { Style = GOLogTextStyle.Trace });
        var entry = new GOLogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, new string('c', 80), default,
            "line\r\n\t\u001b[31m\u2028\u2029", applicationName: new string('p', 80));
        var text = formatter.Format(entry);
        Assert.Contains(new string('c', 29) + "~", text);
        Assert.Contains(new string('p', 41) + "~", text);
        Assert.Contains("\\r\\n\\t\\u001b[31m\\u2028\\u2029", text);
        Assert.DoesNotContain('\u001b', text);
    }
    [Theory]
    [InlineData("X")]
    [InlineData("%")]
    [InlineData("")]
    [InlineData("yyyy\n")]
    [InlineData("yyyy\u001b")]
    [InlineData("yyyy\u2028")]
    public void RejectsInvalidOrUnsafeTimestampFormats(string format) =>
        Assert.Throws<GOLogConfigurationException>(() => new GOTextFormatter(new() { TimestampFormat = format }));

    [Fact]
    public void RejectsInvalidPresentationConfiguration()
    {
        Assert.Throws<GOLogConfigurationException>(() => new GOTextFormatter(new() { Style = (GOLogTextStyle)99 }));
        Assert.Throws<GOLogConfigurationException>(() => new GOTextFormatter(new() { TimestampFormat = new string('y', 129) }));
        Assert.Throws<GOLogConfigurationException>(() => new GOLogOptions { File = new() { Text = null! } }.Validate());
        Assert.Throws<GOLogConfigurationException>(() => new GOLogOptions { Console = null! }.Validate());
        Assert.Throws<GOLogConfigurationException>(() => new GOLogOptions { Console = new() { StandardErrorFromLevel = (LogLevel)99 } }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GOLogOperationContext("op", -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GOLogOperationContext("op", 65, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GOLogOperationContext("op", 0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GOLogOperationContext("op", 0, -1));
        Assert.Throws<ArgumentNullException>(() => new GOLogExecutionContext(0, null!, 0, null));
    }
}
