using System.Text.Json;
using mGO.Log.Configuration;

namespace mGO.Log.Internal;

internal static class OptionsSnapshot
{
    public static GOLogOptions Copy(GOLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return JsonSerializer.Deserialize<GOLogOptions>(JsonSerializer.Serialize(options))!;
    }
}
