using mGO.Log.Abstractions.Models;
using mGO.Log.Configuration;
using mGO.Log.Providers;
using mGO.Log.Sinks;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Tests;

public sealed class ConsoleSinkTests
{
    [Theory]
    [InlineData(LogLevel.Warning, 1, 2)]
    [InlineData(LogLevel.None, 3, 0)]
    public async Task RoutesLevelsAndNeverColorsOrDisposesInjectedWriters(LogLevel threshold, int outputCount, int errorCount)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = new GOLogConsoleOptions { StandardErrorFromLevel = threshold, UseColors = true };
        var sink = new GOConsoleSink(options, output, error);
        options.StandardErrorFromLevel = LogLevel.Trace;
        await sink.WriteBatchAsync(new[] { LogLevel.Information, LogLevel.Warning, LogLevel.Error }
            .Select(level => new GOLogEntry(DateTimeOffset.UtcNow, level, "Test", default, "message\u001b[31m\n")).ToArray());
        await sink.FlushAsync();
        await sink.DisposeAsync();
        await sink.DisposeAsync();
        Assert.Equal(outputCount, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(errorCount, error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain('\u001b', output.ToString() + error);
        Assert.Contains("\\u001b[31m\\n", output.ToString());
        output.Write("still open"); error.Write("still open");
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.WriteBatchAsync([]));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.FlushAsync());
    }
    [Fact]
    public async Task ProcessorMasksValuesBeforeConsoleOutput()
    {
        using var output = new StringWriter();
        await using var provider = new GOLoggerProvider(new(), [new GOConsoleSink(new() { StandardErrorFromLevel = LogLevel.None }, output, output)]);
        provider.CreateLogger("Test").LogInformation("Token {Token}", "sensitive-value");
        await provider.FlushAsync();
        Assert.DoesNotContain("sensitive-value", output.ToString());
        Assert.Contains("***", output.ToString());
    }
    [Fact]
    public async Task CancellationAndWriterFailuresAreObservable()
    {
        using var writer = new StringWriter();
        await using var sink = new GOConsoleSink(new(), writer, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sink.WriteBatchAsync(
            [new(DateTimeOffset.UtcNow, LogLevel.Information, "Test", default, "message")], cancellation.Token));
        await using var provider = new GOLoggerProvider(new(), [new GOConsoleSink(new(), new ThrowingWriter(), new ThrowingWriter())]);
        provider.CreateLogger("Test").LogInformation("failure");
        await provider.FlushAsync();
        Assert.Equal(1, provider.Diagnostics.Failed);
    }
    private sealed class ThrowingWriter : StringWriter
    {
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Cannot write");
    }
}
