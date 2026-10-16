using GO.Log.Diagnostics;

namespace Microsoft.Extensions.Logging;

/// <summary>Explicit method/workflow tracing without stack walking or compile-time weaving.</summary>
public static class GOLogOperationExtensions
{
    /// <summary>Logs entry/exit and tracks monotonic elapsed time in a scope that flows across awaits.</summary>
    public static GOLogOperation BeginGOLogOperation(this ILogger logger, string operationName, LogLevel level = LogLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        if (!Enum.IsDefined(level) || level == LogLevel.None) throw new ArgumentOutOfRangeException(nameof(level));
        return new GOLogOperation(logger, operationName, level);
    }
}
