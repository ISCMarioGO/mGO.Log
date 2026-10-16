using System.Text.Json;
using GO.Log.Configuration;

namespace GO.Log.Internal;

internal static class OptionsSnapshot
{
    public static GOLogOptions Copy(GOLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return JsonSerializer.Deserialize<GOLogOptions>(JsonSerializer.Serialize(options))!;
    }
}
