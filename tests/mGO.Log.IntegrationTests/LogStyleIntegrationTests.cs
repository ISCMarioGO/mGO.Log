using System.Text.Json;
using mGO.Log.Configuration;
using mGO.Log.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace mGO.Log.IntegrationTests;

public sealed class LogStyleIntegrationTests
{
    [Theory]
    [InlineData(GOLogTextStyle.Standard, "[Debug]")]
    [InlineData(GOLogTextStyle.Compact, "[DBG]")]
    [InlineData(GOLogTextStyle.Trace, " |>")]
    public async Task FileSinkUsesConfiguredStyleAndOperationContext(GOLogTextStyle style, string expected)
    {
        using var directory = new TestDirectory();
        await using (var provider = new GOLoggerProvider(new()
        {
            MinimumLevel = LogLevel.Debug,
            File = new() { Directory = directory.Path, Format = GOLogFileFormat.Text, Text = new() { Style = style } }
        }))
        {
            var logger = provider.CreateLogger("StyleTest");
            using (logger.BeginGOLogOperation("SaveOrder"))
            {
                await Task.Yield();
                logger.LogWarning("Password {Password}", "do-not-store");
            }
        }
        var lines = directory.Lines();
        Assert.Equal(3, lines.Length);
        Assert.Contains(expected, lines[0]);
        Assert.Contains("Entering SaveOrder", lines[0]);
        Assert.Contains("Leaving SaveOrder", lines[2]);
        Assert.DoesNotContain("do-not-store", string.Join("\n", lines));
    }
    [Fact]
    public async Task JsonLinesRetainStructuredProducerAndOperationFields()
    {
        using var directory = new TestDirectory();
        await using (var provider = new GOLoggerProvider(new()
        {
            MinimumLevel = LogLevel.Debug, File = new() { Directory = directory.Path }
        }))
        {
            using (provider.CreateLogger("Test").BeginGOLogOperation("JSON")) { await Task.Yield(); }
        }
        var events = directory.Lines().Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal("Enter", events[0].GetProperty("EventKind").GetString());
        Assert.Equal("Leave", events[1].GetProperty("EventKind").GetString());
        Assert.Equal("JSON", events[1].GetProperty("Operation").GetProperty("Name").GetString());
        Assert.Equal(Environment.ProcessId, events[0].GetProperty("ExecutionContext").GetProperty("ProcessId").GetInt32());
    }
    [Fact]
    public async Task HostBindsStylesAndAddsConsoleAlongsideFileWithoutAnotherEngine()
    {
        using var directory = new TestDirectory();
        var settings = System.IO.Path.Combine(directory.Path, "appsettings.json");
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new
        {
            GOLog = new
            {
                ApplicationName = "Styles.Host", MinimumLevel = "Debug",
                File = new { Directory = directory.Path, Format = "Text", Text = new { Style = "Trace" } },
                Console = new { Enabled = true, UseColors = false, StandardErrorFromLevel = "Warning", Text = new { Style = "Compact" } }
            }
        }));
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"));
        using var host = builder.Build();
        await host.StartAsync();
        var logger = host.Services.GetRequiredService<ILogger<LogStyleIntegrationTests>>();
        using (logger.BeginGOLogOperation("Hosted operation", LogLevel.Information)) logger.LogInformation("host style message");
        await host.StopAsync();
        Assert.Single(host.Services.GetServices<GOLoggerProvider>());
        Assert.Contains(directory.Lines(), line => line.StartsWith("> ", StringComparison.Ordinal) && line.Contains("Hosted operation"));
        Assert.Equal(0, host.Services.GetRequiredService<GOLoggerProvider>().Diagnostics.Failed);
    }
}
