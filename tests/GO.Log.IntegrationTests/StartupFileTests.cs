using System.Text.Json;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using GO.Log.Sinks;
using Microsoft.Extensions.Logging;

namespace GO.Log.IntegrationTests;

public sealed class StartupFileTests
{
    private static GOLogEntry Entry(string message) => new(DateTimeOffset.UtcNow, LogLevel.Information, "Restart", default, message);
    private static GOLogOptions Options(TestDirectory directory, GOLogFileFormat format = GOLogFileFormat.Json) =>
        new() { File = new() { Directory = directory.Path, Format = format, AppendOnStartup = true } };

    [Theory]
    [InlineData(GOLogFileFormat.Json)]
    [InlineData(GOLogFileFormat.Text)]
    public async Task AppendOnStartupPreservesExistingBytesAndKeepsSameFile(GOLogFileFormat format)
    {
        using var directory = new TestDirectory();
        var options = Options(directory, format);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var path = Assert.Single(directory.Logs());
        var original = await File.ReadAllBytesAsync(path);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("after")]);
        Assert.Equal(path, Assert.Single(directory.Logs()));
        var appended = await File.ReadAllBytesAsync(path);
        Assert.Equal(original, appended[..original.Length]);
        Assert.Equal(2, directory.Lines().Length);
        if (format == GOLogFileFormat.Json)
        {
            var messages = directory.Lines().Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("Message").GetString());
            Assert.Equal(new[] { "before", "after" }, messages);
        }
    }
    [Fact]
    public async Task AppendSelectsTheLatestCompatibleFile()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        options.File.AppendOnStartup = false;
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("first")]);
        var first = Assert.Single(directory.Logs());
        var original = await File.ReadAllBytesAsync(first);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("second")]);
        var second = directory.Logs().Single(path => path != first);
        File.SetLastWriteTimeUtc(first, clock.Now.AddMinutes(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(second, clock.Now.AddMinutes(-1).UtcDateTime);
        options.File.AppendOnStartup = true;
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("resumed")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
        Assert.Equal(2, (await File.ReadAllLinesAsync(second)).Length);
    }
    [Fact]
    public async Task AppendOnStartupStillRotatesOnUtcDayChange()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("yesterday")]);
        var first = Assert.Single(directory.Logs());
        var original = await File.ReadAllBytesAsync(first);
        clock.Now = clock.Now.AddDays(1);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("today")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
    }
    [Fact]
    public async Task AppendOnStartupStillRotatesWhenNextEventWouldExceedSize()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var first = Assert.Single(directory.Logs());
        var original = await File.ReadAllBytesAsync(first);
        options.File.MaxFileSizeMB = (original.Length + 1) / (1024d * 1024d);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("after")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
    }
    [Fact]
    public async Task ResumedFileContinuesRotatingWithinTheSameRun()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var first = Assert.Single(directory.Logs());
        var firstSize = new FileInfo(first).Length;
        options.File.MaxFileSizeMB = (firstSize * 2 + 20) / (1024d * 1024d);
        await using (var sink = new GOFileSink(options, timeProvider: clock))
        {
            await sink.WriteBatchAsync([Entry("after")]);
            await sink.FlushAsync();
            Assert.Single(directory.Logs());
            await sink.WriteBatchAsync([Entry(new string('x', 100))]);
        }
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(2, (await File.ReadAllLinesAsync(first)).Length);
    }
    [Fact]
    public async Task AppendOnStartupDoesNotMixFormats()
    {
        using var directory = new TestDirectory();
        var options = Options(directory, GOLogFileFormat.Text);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("text")]);
        var first = Assert.Single(directory.Logs());
        var original = await File.ReadAllBytesAsync(first);
        options.File.Format = GOLogFileFormat.Json;
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("json")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
        var jsonPath = directory.Logs().Single(p => p != first);
        using var json = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(jsonPath)));
        Assert.Equal("json", json.RootElement.GetProperty("Message").GetString());
    }
    [Fact]
    public async Task TruncatedFileIsPreservedAndNotAppended()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var first = Assert.Single(directory.Logs());
        await File.AppendAllTextAsync(first, "{\"incomplete\":");
        var original = await File.ReadAllBytesAsync(first);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("after")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyOwnedFilesWithKnownFormatAreResumed(bool legacyMarker)
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var first = Assert.Single(directory.Logs());
        var marker = first + ".golog-managed";
        if (legacyMarker) await File.WriteAllTextAsync(marker, (await File.ReadAllTextAsync(marker)).Split('|')[0]);
        else File.Delete(marker);
        var original = await File.ReadAllBytesAsync(first);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("after")]);
        Assert.Equal(2, directory.Logs().Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(first));
    }
    [Fact]
    public async Task AppendCanResumeAnEmptyOwnedFile()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var clock = new StartupClock();
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("before")]);
        var path = Assert.Single(directory.Logs());
        await File.WriteAllBytesAsync(path, []);
        await using (var sink = new GOFileSink(options, timeProvider: clock)) await sink.WriteBatchAsync([Entry("after")]);
        Assert.Equal(path, Assert.Single(directory.Logs()));
        Assert.Single(directory.Lines());
    }
    private sealed class StartupClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
