using Microsoft.Extensions.Logging;
using Trace = GO.Log.Attributes.Trace;

internal sealed class AttributedWorker(ILogger<AttributedWorker> logger)
{
    private readonly ILogger<AttributedWorker> _logger = logger;

    [Trace.Information]
    public async Task RunAsync()
    {
        await Task.Yield();
        Validate();
        await SaveAsync();
    }

    [Trace.Debug]
    private void Validate() => _logger.LogInformation("Validating item {ItemId}", 42);

    [Trace.Debug]
    private async Task SaveAsync()
    {
        await Task.Delay(10);
        _logger.LogInformation("Saved item {ItemId}", 42);
    }
}
