using Microsoft.Extensions.Logging;

namespace mGO.Log.Configuration;

/// <summary>Optional asynchronous console destination settings.</summary>
public sealed class GOLogConsoleOptions
{
    /// <summary>Enables the console destination in addition to configured file output.</summary>
    public bool Enabled { get; set; }
    /// <summary>Uses ANSI severity colors only for an interactive, non-redirected console.</summary>
    public bool UseColors { get; set; } = true;
    /// <summary>Routes this level and above to stderr. None sends all events to stdout.</summary>
    public LogLevel StandardErrorFromLevel { get; set; } = LogLevel.Warning;
    /// <summary>Console text presentation.</summary>
    public GOLogTextOptions Text { get; set; } = new() { Style = GOLogTextStyle.Compact };
}
