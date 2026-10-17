using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using mGO.Log.Abstractions.Interfaces;
using mGO.Log.Abstractions.Models;
using mGO.Log.Configuration;
using mGO.Log.Formatters;
using mGO.Log.Providers;
using Microsoft.Extensions.Logging;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

[MemoryDiagnoser]
public class CaptureBenchmarks
{
    private GOLoggerProvider _provider = null!;
    private ILogger _logger = null!;
    private readonly Exception _exception = new InvalidOperationException("Benchmark exception");
    [GlobalSetup]
    public void Setup()
    {
        _provider = new GOLoggerProvider(new GOLogOptions { File = new() { Enabled = false }, Queue = new() { Capacity = 65536 } }, [new NullSink()]);
        _logger = _provider.CreateLogger("Benchmark");
    }
    [Benchmark(Baseline = true)] public void Information() => _logger.LogInformation("Hello");
    [Benchmark] public void Error() => _logger.LogError(_exception, "Failure");
    [Benchmark] public void Structured() => _logger.LogInformation("User {UserId} completed {Count}", 42, 7);
    [IterationCleanup]
    public async Task Drain()
    {
        await _provider.FlushAsync();
        if (_provider.Diagnostics.Dropped != 0 || _provider.Diagnostics.Failed != 0)
            throw new InvalidOperationException("Capture benchmark saturated; increase capacity before interpreting results.");
    }
    [GlobalCleanup] public void Cleanup() => _provider.Dispose();
}

[MemoryDiagnoser]
public class FormatterBenchmarks
{
    private readonly GOLogEntry _entry = new(DateTimeOffset.UtcNow, LogLevel.Information, "Benchmark", new EventId(1), "Hello world");
    private readonly GOTextFormatter _text = new();
    private readonly GOTextFormatter _compact = new(new() { Style = GOLogTextStyle.Compact });
    private readonly GOTextFormatter _trace = new(new() { Style = GOLogTextStyle.Trace });
    private readonly GOJsonFormatter _json = new();
    [Benchmark(Baseline = true)] public string Text() => _text.Format(_entry);
    [Benchmark] public string TextCompact() => _compact.Format(_entry);
    [Benchmark] public string TextTrace() => _trace.Format(_entry);
    [Benchmark] public string Json() => _json.Format(_entry);
}

// Includes producer capture, consumer processing, actual writes, flush and shutdown.
[MemoryDiagnoser]
public class DiskThroughputBenchmarks
{
    [Params(1, 64, 256)] public int BatchSize { get; set; }
    [Params(GOLogFileFormat.Text, GOLogFileFormat.Json)] public GOLogFileFormat Format { get; set; }
    [Benchmark(OperationsPerInvoke = 1000)]
    public async Task WriteBatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "golog-bench-" + Guid.NewGuid());
        try
        {
            await using var provider = new GOLoggerProvider(new GOLogOptions
            {
                File = new() { Directory = directory, Format = Format },
                Queue = new() { Capacity = 2048, BatchSize = BatchSize }
            });
            var logger = provider.CreateLogger("Throughput");
            for (var i = 0; i < 1000; i++) logger.LogInformation("Event {Index}", i);
            await provider.FlushAsync();
            if (provider.Diagnostics.Dropped != 0 || provider.Diagnostics.Failed != 0)
                throw new InvalidOperationException("The throughput run lost events.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

internal sealed class NullSink : IGOLogSink
{
    public ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
