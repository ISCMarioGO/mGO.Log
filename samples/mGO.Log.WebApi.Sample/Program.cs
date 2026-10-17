using System.Diagnostics;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"));
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/test-log", (ILogger<Program> logger) =>
{
    using var operation = logger.BeginGOLogOperation("TestLog", LogLevel.Information);
    using var scope = logger.BeginScope(new Dictionary<string, object?> { ["Operation"] = "TestLog" });
    logger.LogInformation("Hello user {UserId}", 42);
    return Results.Ok(new { traceId = Activity.Current?.TraceId.ToString() });
});
app.MapGet("/test-error", (ILogger<Program> logger) =>
{
    try { throw new InvalidOperationException("Sample error", new Exception("Inner sample error")); }
    catch (Exception ex)
    {
        logger.LogError(ex, "Request failed for {Operation}", "TestError");
        return Results.Problem("Sample error recorded", statusCode: 500);
    }
});
app.Run();
