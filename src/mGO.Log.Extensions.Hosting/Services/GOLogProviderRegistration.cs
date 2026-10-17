using mGO.Log.Providers;
using Microsoft.Extensions.Logging;

namespace mGO.Log.Extensions.Hosting.Services;

// DI owns the concrete engine. The registration wrapper forwards scopes without owning a second engine.
[ProviderAlias("GOLog")]
internal sealed class GOLogProviderRegistration(GOLoggerProvider provider) : ILoggerProvider, ISupportExternalScope
{
    public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => provider.SetScopeProvider(scopeProvider);
    public void Dispose() { }
}
