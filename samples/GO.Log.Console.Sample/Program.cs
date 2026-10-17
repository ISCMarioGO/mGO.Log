using GO.Log.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"));
using var host = builder.Build();
await host.StartAsync();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
using (logger.BeginScope(new Dictionary<string, object?> { ["Operation"] = "ConsoleSample" }))
{
    using var operation = logger.BeginGOLogOperation("ConsoleSample", LogLevel.Information);
    foreach (var level in Enum.GetValues<LogLevel>().Where(l => l != LogLevel.None))
        logger.Log(level, "Example {Level} for user {UserId}", level, 42);
    try { throw new InvalidOperationException("Example failure", new Exception("Inner context")); }
    catch (Exception ex) { logger.LogError(ex, "Failed operation {OperationId}", 7); }
    await Parallel.ForEachAsync(Enumerable.Range(0, 100), (i, _) =>
    {
        logger.LogInformation("Concurrent event {Index}; token {Token}", i, "sample-sensitive-value");
        return ValueTask.CompletedTask;
    });
}

await new AttributedWorker(host.Services.GetRequiredService<ILogger<AttributedWorker>>()).RunAsync();
await host.Services.GetRequiredService<GOLoggerProvider>().FlushAsync();
await host.StopAsync();
System.Console.WriteLine("Logs written to logs/. Host stopped cleanly.");
