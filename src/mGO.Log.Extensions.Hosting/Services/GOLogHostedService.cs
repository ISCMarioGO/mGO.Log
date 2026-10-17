using mGO.Log.Providers;
using Microsoft.Extensions.Hosting;

namespace mGO.Log.Extensions.Hosting.Services;

internal sealed class GOLogHostedService(GOLoggerProvider provider) : IHostedService
{
    // Resolving the provider starts its supervised consumer, including startup logs.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => provider.StopAsync(cancellationToken);
}
