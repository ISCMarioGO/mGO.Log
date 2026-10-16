using System.Collections;
using System.Text.Json;
using GO.Log.Abstractions.Models;
using GO.Log.Configuration;
using GO.Log.Exceptions;
using GO.Log.Formatters;
using GO.Log.Providers;
using Microsoft.Extensions.Logging;

namespace GO.Log.Tests;

public sealed class SecurityAndFormatterTests
{
    [Fact]
    public void FormatsTextAndJsonWithoutRawNewlinesAndPreservesContext()
    {
        var properties = new Dictionary<string, JsonElement> { ["UserId"] = JsonSerializer.SerializeToElement(42) };
        var entry = new GOLogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "Cat\nTest", new EventId(5, "Failure"),
            "hello\nworld", "hello {UserId}", properties, "Type", "error", "outer\ninner", "trace", "span",
            [JsonSerializer.SerializeToElement(new { Scope = "scope" })], "App");
        properties.Clear();
        var json = new GOJsonFormatter().Format(entry);
        Assert.DoesNotContain('\n', json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("Error", parsed.RootElement.GetProperty("LogLevel").GetString());
        Assert.Equal("hello\nworld", parsed.RootElement.GetProperty("Message").GetString());
        Assert.Equal("Failure", parsed.RootElement.GetProperty("EventId").GetProperty("Name").GetString());
        Assert.Equal(42, parsed.RootElement.GetProperty("StructuredProperties").GetProperty("UserId").GetInt32());
        var text = new GOTextFormatter().Format(entry);
        Assert.DoesNotContain('\n', text);
        Assert.Contains("[Error]", text);
        Assert.Contains("hello\\nworld", text);
        Assert.Contains("TraceId=trace SpanId=span", text);
        Assert.Contains("outer\\ninner", text);
        Assert.Contains("Scopes=", text);
        Assert.Contains("Template=", text);
        Assert.Contains("Properties=", text);
        Assert.Contains("Information", new GOTextFormatter().Format(new(DateTimeOffset.UtcNow, LogLevel.Information, "cat", default, "msg")));
    }
    [Theory]
    [InlineData("password")]
    [InlineData("TOKEN")]
    [InlineData("Authorization")]
    [InlineData("Secret")]
    [InlineData("apiKEY")]
    [InlineData("ConnectionString")]
    public async Task MasksCaseInsensitiveKeysInStateAndRenderedMessage(string key)
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new(), [sink]);
        provider.CreateLogger("Test").Log(LogLevel.Information, default,
            new[] { KeyValuePair.Create<string, object?>(key, "hidden") }, null, (_, _) => "value hidden");
        await provider.FlushAsync();
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("***", entry.StructuredProperties[key].GetString());
        Assert.Equal("value ***", entry.Message);
    }
    [Fact]
    public async Task EnforcesDepthItemCountAndStringLimitsOnDictionariesArraysAndObjects()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new()
        {
            Security = new() { MaxPropertyDepth = 2, MaxProperties = 2, MaxMessageLength = 12 }
        }, [sink]);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("{Object}", new Hashtable { ["Items"] = new[] { 1, 2, 3 }, ["Nested"] = new { Value = new { Again = 1 } } });
        logger.LogInformation("{Items}", (object)new[] { "longlonglonglong", "two", "three" });
        logger.LogInformation("{One} {Two} {Three}", 1, 2, 3);
        await provider.FlushAsync();
        var entries = sink.Entries.ToArray();
        Assert.All(entries, e => Assert.InRange(e.Message.Length, 0, 12));
        Assert.Equal(2, entries[0].StructuredProperties["Object"].GetProperty("Items").GetArrayLength());
        Assert.Equal("[MaxDepth]", entries[0].StructuredProperties["Object"].GetProperty("Nested").GetProperty("Value").GetString());
        Assert.Equal(2, entries[1].StructuredProperties["Items"].GetArrayLength());
        Assert.Equal(12, entries[1].StructuredProperties["Items"][0].GetString()!.Length);
        Assert.Equal(2, entries[2].StructuredProperties.Count);
    }
    [Fact]
    public async Task MaskingCanBeDisabledAndFreeTextIsPreserved()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { Security = new() { MaskSensitiveData = false } }, [sink]);
        provider.CreateLogger("Test").LogInformation("Secret text {Password}", "visible");
        await provider.FlushAsync();
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("visible", entry.StructuredProperties["Password"].GetString());
        Assert.Equal("Secret text visible", entry.Message);
    }
    [Theory]
    [InlineData("application")]
    [InlineData("level")]
    [InlineData("group")]
    [InlineData("capacity")]
    [InlineData("batch")]
    [InlineData("timeout")]
    [InlineData("fullmode")]
    [InlineData("directory")]
    [InlineData("pattern")]
    [InlineData("tokens")]
    [InlineData("format")]
    [InlineData("size")]
    [InlineData("nan")]
    [InlineData("retention")]
    [InlineData("count")]
    [InlineData("flush")]
    [InlineData("depth")]
    [InlineData("length")]
    [InlineData("properties")]
    [InlineData("keys")]
    public void RejectsInvalidConfiguration(string setting)
    {
        var options = new GOLogOptions();
        switch (setting)
        {
            case "application": options.ApplicationName = " "; break;
            case "level": options.MinimumLevel = (LogLevel)99; break;
            case "group": options.Security = null!; break;
            case "capacity": options.Queue.Capacity = 0; break;
            case "batch": options.Queue.BatchSize = 0; break;
            case "timeout": options.Queue.ShutdownTimeoutSeconds = 0; break;
            case "fullmode": options.Queue.FullMode = (GOLogQueueFullMode)99; break;
            case "directory": options.File.Directory = " "; break;
            case "pattern": options.File.FileNamePattern = "../file.log"; break;
            case "tokens": options.File.FileNamePattern = "{unknown}-{date}-{sequence}"; break;
            case "format": options.File.Format = (GOLogFileFormat)99; break;
            case "size": options.File.MaxFileSizeMB = 0; break;
            case "nan": options.File.MaxFileSizeMB = double.NaN; break;
            case "retention": options.File.RetentionDays = 0; break;
            case "count": options.File.MaxRetainedFiles = 0; break;
            case "flush": options.File.FlushIntervalSeconds = 0; break;
            case "depth": options.Security.MaxPropertyDepth = 33; break;
            case "length": options.Security.MaxMessageLength = 0; break;
            case "properties": options.Security.MaxProperties = 0; break;
            case "keys": options.Security.SensitiveKeys = [""]; break;
        }
        Assert.Throws<GOLogConfigurationException>(options.Validate);
    }
    [Fact]
    public void DefaultsAndArgumentValidation()
    {
        new GOLogOptions().Validate();
        Assert.False(new GOLogOptions().File.AppendOnStartup);
        Assert.Throws<ArgumentNullException>(() => new GOLoggerProvider(null!));
        Assert.Throws<ArgumentNullException>(() => new GOJsonFormatter().Format(null!));
        Assert.Throws<ArgumentNullException>(() => new GOTextFormatter().Format(null!));
        using var provider = new GOLoggerProvider(new(), []);
        Assert.Throws<ArgumentNullException>(() => provider.CreateLogger(null!));
        Assert.Throws<ArgumentNullException>(() => provider.SetScopeProvider(null!));
        Assert.Throws<ArgumentException>(() => new GOLoggerProvider(new(), [null!]));
        Assert.Throws<ArgumentNullException>(() => provider.CreateLogger("Test").Log<string>(LogLevel.Error, default, "x", null, null!));
    }
}
