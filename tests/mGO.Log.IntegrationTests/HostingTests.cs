using System.Net;
using System.Text.Json;
using mGO.Log.Configuration;
using mGO.Log.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mGO.Log.IntegrationTests;

public sealed class HostingTests
{
    [Fact]
    public async Task RepeatedRegistrationCreatesOneEngineAndBindsConfiguration()
    {
        using var directory = new TestDirectory();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GOLog:ApplicationName"] = "Integration.Host",
            ["GOLog:MinimumLevel"] = "Warning",
            ["GOLog:File:Directory"] = directory.Path,
            ["GOLog:File:AppendOnStartup"] = "true",
            ["GOLog:Queue:BatchSize"] = "5"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"));
        builder.Logging.AddGOLog(o => o.IncludeActivityTracking = false);
        using var host = builder.Build();
        await host.StartAsync();
        var providers = host.Services.GetServices<ILoggerProvider>();
        Assert.Single(providers);
        Assert.Single(host.Services.GetServices<GOLoggerProvider>());
        Assert.Equal(5, host.Services.GetRequiredService<IOptions<GOLogOptions>>().Value.Queue.BatchSize);
        Assert.True(host.Services.GetRequiredService<IOptions<GOLogOptions>>().Value.File.AppendOnStartup);
        var logger = host.Services.GetRequiredService<ILogger<HostingTests>>();
        logger.LogInformation("filtered event");
        logger.LogWarning("Saved {Value}", 42);
        await host.StopAsync();
        var line = Assert.Single(directory.Lines());
        using var json = JsonDocument.Parse(line);
        Assert.Equal("Integration.Host", json.RootElement.GetProperty("ApplicationName").GetString());
        Assert.Equal("Saved 42", json.RootElement.GetProperty("Message").GetString());
    }
    [Fact]
    public void InvalidOptionsFailBeforeTheHostRuns()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddGOLog(o => o.Queue.Capacity = 0);
        Assert.Throws<OptionsValidationException>(() => builder.Build());
    }
    [Fact]
    public async Task AspNetCoreServesRealHttpRequestsWithScopesExceptionsAndActivity()
    {
        using var directory = new TestDirectory();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Logging.AddGOLog(o =>
        {
            o.ApplicationName = "Integration.Web";
            o.File.Directory = directory.Path;
        });
        await using var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/test-log", (ILogger<HostingTests> logger) =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?> { ["RequestScope"] = "present" });
            logger.LogInformation("Request {UserId}", 42);
            return Results.Ok();
        });
        app.MapGet("/test-error", (ILogger<HostingTests> logger) =>
        {
            try { throw new InvalidOperationException("request failure"); }
            catch (Exception ex) { logger.LogError(ex, "Request error {Code}", 17); return Results.StatusCode(500); }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-log")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/test-error")).StatusCode);
        await app.StopAsync();
        var events = directory.Lines().Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var request = Assert.Single(events, e => e.GetProperty("Message").GetString() == "Request 42");
        Assert.NotEmpty(request.GetProperty("TraceId").GetString()!);
        Assert.NotEmpty(request.GetProperty("SpanId").GetString()!);
        Assert.Contains("present", request.GetProperty("ScopeProperties").GetRawText());
        var error = Assert.Single(events, e => e.GetProperty("Message").GetString() == "Request error 17");
        Assert.Contains("request failure", error.GetProperty("ExceptionDetails").GetString());
    }
}
