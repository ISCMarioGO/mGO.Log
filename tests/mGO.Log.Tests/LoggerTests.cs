using System.Diagnostics;
using System.Text.Json;
using mGO.Log.Configuration;
using mGO.Log.Providers;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Tests;

public sealed class LoggerTests
{
    [Theory]
    [InlineData(LogLevel.Trace, true)]
    [InlineData(LogLevel.Debug, true)]
    [InlineData(LogLevel.Information, true)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    [InlineData((LogLevel)(-1), false)]
    [InlineData((LogLevel)99, false)]
    public async Task AllLevelsRespectMinimumAndNone(LogLevel level, bool expected)
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var logger = provider.CreateLogger("Category");
        Assert.Equal(expected, logger.IsEnabled(level));
        logger.Log(level, new EventId(12, "Named"), "Message");
        await provider.FlushAsync();
        Assert.Equal(expected ? 1 : 0, sink.Entries.Count);
        if (expected) Assert.Equal(new EventId(12, "Named"), Assert.Single(sink.Entries).EventId);
    }
    [Theory]
    [InlineData(false, LogLevel.Trace)]
    [InlineData(true, LogLevel.None)]
    [InlineData(true, LogLevel.Error)]
    public async Task DisabledAndFilteredCallsDoNotEvaluateFormatter(bool enabled, LogLevel minimum)
    {
        await using var provider = new GOLoggerProvider(new() { Enabled = enabled, MinimumLevel = minimum }, []);
        provider.CreateLogger("Test").Log(LogLevel.Information, default, 1, null, (_, _) => throw new Exception("Must not execute"));
        await provider.FlushAsync();
        Assert.Equal(0, provider.Diagnostics.CaptureFailures);
        Assert.Equal(0, provider.Diagnostics.Processed);
    }
    [Fact]
    public async Task CapturesPropertiesScopesExceptionAndActivityBeforeCallerMutates()
    {
        var sink = new BlockingSink();
        await using var provider = new GOLoggerProvider(new() { MinimumLevel = LogLevel.Trace }, [sink]);
        var logger = provider.CreateLogger("Orders");
        var mutable = new Dictionary<string, object?> { ["Name"] = "before", ["Token"] = "hidden" };
        using var activity = new Activity("Test").SetIdFormat(ActivityIdFormat.W3C).Start();
        Exception exception;
        try { throw new InvalidOperationException("outer", new Exception("inner")); }
        catch (Exception ex) { exception = ex; }
        using (logger.BeginScope(new Dictionary<string, object?> { ["scope"] = mutable }))
            logger.LogError(new EventId(8, "OrderError"), exception, "Order {OrderId}: {Details}", 42, mutable);
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        mutable["Name"] = "after";
        sink.Release.TrySetResult();
        await provider.FlushAsync();
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("Orders", entry.Category);
        Assert.Equal(42, entry.StructuredProperties["OrderId"].GetInt32());
        Assert.Equal("before", entry.StructuredProperties["Details"].GetProperty("Name").GetString());
        Assert.Equal("***", entry.StructuredProperties["Details"].GetProperty("Token").GetString());
        Assert.Equal("before", entry.ScopeProperties[0].GetProperty("scope").GetProperty("Name").GetString());
        Assert.Equal(activity.TraceId.ToString(), entry.TraceId);
        Assert.Equal(activity.SpanId.ToString(), entry.SpanId);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionType);
        Assert.Equal("outer", entry.ExceptionMessage);
        Assert.Contains("inner", entry.ExceptionDetails);
        Assert.Contains(nameof(CapturesPropertiesScopesExceptionAndActivityBeforeCallerMutates), entry.ExceptionDetails);
        Assert.Equal("Order {OrderId}: {Details}", entry.MessageTemplate);
        Assert.Equal(TimeSpan.Zero, entry.Timestamp.Offset);
    }
    [Fact]
    public async Task ExternalScopeAndOptionsAreCopied()
    {
        var sink = new MemorySink();
        var options = new GOLogOptions { IncludeActivityTracking = false };
        await using var provider = new GOLoggerProvider(options, [sink]);
        var scopes = new LoggerExternalScopeProvider();
        provider.SetScopeProvider(scopes);
        options.Enabled = false;
        options.Security.SensitiveKeys[0] = "other";
        using (scopes.Push(new { Password = "hidden", Count = 3 }))
            provider.CreateLogger("Test").LogInformation("Hello {Password}", "hidden");
        await provider.FlushAsync();
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("Hello ***", entry.Message);
        Assert.Equal("***", entry.ScopeProperties[0].GetProperty("Password").GetString());
        Assert.Null(entry.TraceId);
    }
    [Fact]
    public async Task CapturesUnstructuredScopeAndCanDisableScopes()
    {
        var sink = new MemorySink();
        await using (var provider = new GOLoggerProvider(new(), [sink]))
        {
            var logger = provider.CreateLogger("Test");
            using (logger.BeginScope("scope text")) logger.LogInformation("Hello");
            await provider.FlushAsync();
            Assert.Equal("scope text", Assert.Single(sink.Entries).ScopeProperties[0].GetString());
        }
        sink = new MemorySink();
        await using var disabled = new GOLoggerProvider(new() { IncludeScopes = false }, [sink]);
        var other = disabled.CreateLogger("Test");
        using (other.BeginScope("ignored")) other.LogInformation("Hello");
        await disabled.FlushAsync();
        Assert.Empty(Assert.Single(sink.Entries).ScopeProperties);
    }
    [Fact]
    public async Task CaptureErrorsDoNotEscapeOrRecurse()
    {
        await using var provider = new GOLoggerProvider(new(), []);
        var logger = provider.CreateLogger("Test");
        logger.Log(LogLevel.Error, default, "state", null, (_, _) => throw new InvalidOperationException());
        logger.LogInformation("Object {Object}", new ThrowingObject());
        await provider.FlushAsync();
        Assert.Equal(2, provider.Diagnostics.CaptureFailures);
        Assert.Equal(0, provider.Diagnostics.Processed);
        Assert.NotNull(provider.Diagnostics.LastError);
    }
    private sealed class ThrowingObject { public int Value => throw new InvalidOperationException(); }

    [Fact]
    public async Task JsonStateIsDetachedFromDocumentLifetime()
    {
        var sink = new MemorySink();
        await using var provider = new GOLoggerProvider(new(), [sink]);
        using (var document = JsonDocument.Parse("{\"Name\":\"original\",\"Password\":\"secret\",\"List\":[1,true,null]}"))
            provider.CreateLogger("Test").LogInformation("State {Object}", document.RootElement);
        await provider.FlushAsync();
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("original", entry.StructuredProperties["Object"].GetProperty("Name").GetString());
        Assert.Equal("***", entry.StructuredProperties["Object"].GetProperty("Password").GetString());
        Assert.Equal(3, entry.StructuredProperties["Object"].GetProperty("List").GetArrayLength());
    }
}
