using System.Text.Json;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using GO.Log.Formatters;
using GO.Log.Providers;
using GO.Log.Sinks;
using Microsoft.Extensions.Logging;

namespace GO.Log.IntegrationTests;

public sealed class FileSinkTests
{
    private static GOLogEntry Entry(string text = "hello") => new(DateTimeOffset.UtcNow, LogLevel.Information, "Integration", default, text);
    [Theory]
    [InlineData(GOLogFileFormat.Json)]
    [InlineData(GOLogFileFormat.Text)]
    public async Task WritesRealFilesFlushesAndReleasesHandles(GOLogFileFormat format)
    {
        using var directory = new TestDirectory();
        var provider = new GOLoggerProvider(new() { File = new() { Directory = directory.Path, Format = format } });
        provider.CreateLogger("Files").LogInformation("Hello {Name}", "world");
        await provider.FlushAsync();
        var line = Assert.Single(directory.Lines());
        if (format == GOLogFileFormat.Json)
        {
            using var json = JsonDocument.Parse(line);
            Assert.Equal("Hello world", json.RootElement.GetProperty("Message").GetString());
        }
        else Assert.Contains("Hello world", line);
        await provider.DisposeAsync();
        using var exclusive = File.Open(Assert.Single(directory.Logs()), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }
    [Fact]
    public async Task RotatesBySizeAndRetainsNewestFiles()
    {
        using var directory = new TestDirectory();
        var options = new GOLogOptions { File = new() { Directory = directory.Path, MaxFileSizeMB = 0.001, MaxRetainedFiles = 2 } };
        await using (var sink = new GOFileSink(options))
        {
            for (var i = 0; i < 8; i++) await sink.WriteBatchAsync([Entry(new string('x', 700))]);
            await sink.FlushAsync();
        }
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(2, Directory.GetFiles(directory.Path, "*.golog-managed").Length);
        Assert.Equal(2, directory.Lines().Length);
    }
    [Fact]
    public async Task DailyRotationAndRetentionUseUtcClockAndPreserveUnmanagedFiles()
    {
        using var directory = new TestDirectory();
        var clock = new MutableClock();
        var options = new GOLogOptions { File = new() { Directory = directory.Path, RetentionDays = 1, MaxRetainedFiles = 2 } };
        var unrelated = System.IO.Path.Combine(directory.Path, "Application-20200101-000000.log");
        await File.WriteAllTextAsync(unrelated, "unmanaged");
        var arbitrary = System.IO.Path.Combine(directory.Path, "notes.txt");
        await File.WriteAllTextAsync(arbitrary, "keep me");
        await using (var sink = new GOFileSink(options, timeProvider: clock))
        {
            await sink.WriteBatchAsync([Entry("day 1")]);
            await sink.FlushAsync();
            var first = directory.Logs().Single(p => p != unrelated);
            clock.Now = clock.Now.AddDays(2);
            File.SetLastWriteTimeUtc(first, clock.Now.UtcDateTime.AddDays(-2));
            await sink.WriteBatchAsync([Entry("day 3")]);
            await sink.FlushAsync();
            Assert.False(File.Exists(first));
        }
        Assert.True(File.Exists(unrelated));
        Assert.Equal("keep me", await File.ReadAllTextAsync(arbitrary));
        var log = directory.Logs().Single(p => p != unrelated);
        Assert.Contains(clock.Now.ToString("yyyyMMdd"), log);
    }
    [Fact]
    public async Task DailyRotationKeepsBothDaysWhenWithinRetention()
    {
        using var directory = new TestDirectory();
        var clock = new MutableClock();
        await using var sink = new GOFileSink(new() { File = new() { Directory = directory.Path } }, timeProvider: clock);
        await sink.WriteBatchAsync([Entry("first day")]);
        clock.Now = clock.Now.AddDays(1);
        await sink.WriteBatchAsync([Entry("next day")]);
        await sink.FlushAsync();
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(2, directory.Lines().Length);
    }
    [Fact]
    public async Task RestartNeverOverwritesAndSkipsExistingNames()
    {
        using var directory = new TestDirectory();
        var options = new GOLogOptions { File = new() { Directory = directory.Path } };
        await using (var provider = new GOLoggerProvider(options))
            provider.CreateLogger("Restart").LogInformation("before");
        var firstPath = Assert.Single(directory.Logs());
        var original = await File.ReadAllTextAsync(firstPath);
        await using (var provider = new GOLoggerProvider(options))
            provider.CreateLogger("Restart").LogInformation("after");
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllTextAsync(firstPath));
        Assert.Contains(directory.Lines(), line => line.Contains("after"));
    }
    [Fact]
    public async Task ConcurrentProducersWriteEveryEventExactlyOnceBeforeShutdown()
    {
        using var directory = new TestDirectory();
        var provider = new GOLoggerProvider(new()
        {
            File = new() { Directory = directory.Path }, Queue = new() { Capacity = 10000, BatchSize = 37 }
        });
        var logger = provider.CreateLogger("Concurrent");
        await Parallel.ForEachAsync(Enumerable.Range(0, 1000), (i, _) =>
        {
            logger.LogInformation("Item {Id}", i);
            return ValueTask.CompletedTask;
        });
        await provider.StopAsync();
        Assert.Equal(1000, provider.Diagnostics.Processed);
        Assert.Equal(0, provider.Diagnostics.Dropped);
        var identifiers = directory.Lines().Select(line =>
        {
            using var json = JsonDocument.Parse(line);
            return json.RootElement.GetProperty("StructuredProperties").GetProperty("Id").GetInt32();
        }).ToArray();
        Assert.Equal(1000, identifiers.Length);
        Assert.Equal(1000, identifiers.Distinct().Count());
        await provider.DisposeAsync();
    }
    [Fact]
    public async Task FileAccessFailureIsCountedWithoutRecursiveLogging()
    {
        using var directory = new TestDirectory();
        var blocked = System.IO.Path.Combine(directory.Path, "file-is-not-directory");
        await File.WriteAllTextAsync(blocked, "block");
        await using var provider = new GOLoggerProvider(new() { File = new() { Directory = blocked } });
        provider.CreateLogger("Error").LogError("Cannot write");
        await provider.FlushAsync();
        Assert.Equal(1, provider.Diagnostics.Failed);
        Assert.True(provider.Diagnostics.SinkFailures >= 1);
        Assert.NotNull(provider.Diagnostics.LastError);
        Assert.Empty(directory.Logs());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeasePreventsConcurrentWritersWithSameIdentity(bool appendOnStartup)
    {
        using var directory = new TestDirectory();
        var options = new GOLogOptions { File = new() { Directory = directory.Path, AppendOnStartup = appendOnStartup } };
        await using var first = new GOFileSink(options);
        await using var second = new GOFileSink(options);
        await first.WriteBatchAsync([Entry()]);
        await Assert.ThrowsAsync<IOException>(async () => await second.WriteBatchAsync([Entry()]));
        await first.FlushAsync();
        Assert.Single(directory.Logs());
    }
    [Fact]
    public async Task DisabledFileCreatesNoOutputAndSinkRejectsUseAfterDispose()
    {
        using var directory = new TestDirectory();
        await using (var provider = new GOLoggerProvider(new() { File = new() { Enabled = false, Directory = directory.Path } }))
        {
            provider.CreateLogger("Test").LogInformation("no persistence");
            await provider.FlushAsync();
            Assert.Equal(1, provider.Diagnostics.Processed);
        }
        Assert.Empty(Directory.GetFiles(directory.Path));
        var sink = new GOFileSink(new() { File = new() { Directory = directory.Path } }, new GOTextFormatter());
        await sink.DisposeAsync();
        await sink.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.WriteBatchAsync([Entry()]));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await sink.FlushAsync());
    }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
