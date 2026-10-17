using Microsoft.Extensions.Logging;

namespace GO.Log.Attributes;

/// <summary>Method decorators such as [Trace.Debug] and [Trace.Information]; no global logger state.</summary>
public static class Trace
{
    /// <summary>Registers method entry and exit at Trace severity.</summary>
    public sealed class TraceAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Trace, loggerMemberName, operationName);
    /// <summary>Registers method entry and exit at Debug severity.</summary>
    public sealed class DebugAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Debug, loggerMemberName, operationName);
    /// <summary>Registers method entry and exit at Information severity.</summary>
    public sealed class InformationAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Information, loggerMemberName, operationName);
    /// <summary>Registers method entry and exit at Warning severity.</summary>
    public sealed class WarningAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Warning, loggerMemberName, operationName);
    /// <summary>Registers method entry and exit at Error severity.</summary>
    public sealed class ErrorAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Error, loggerMemberName, operationName);
    /// <summary>Registers method entry and exit at Critical severity.</summary>
    public sealed class CriticalAttribute(string loggerMemberName = "_logger", string? operationName = null)
        : GOTraceAttribute(LogLevel.Critical, loggerMemberName, operationName);
}
