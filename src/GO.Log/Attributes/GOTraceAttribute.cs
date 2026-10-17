using Microsoft.Extensions.Logging;

namespace GO.Log.Attributes;

/// <summary>Instruments method entry/exit at build time using the named ILogger field, property or parameter.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class GOTraceAttribute : Attribute
{
    /// <summary>Creates tracing metadata. The GO.Log build target supplies the executable instrumentation.</summary>
    public GOTraceAttribute(LogLevel level = LogLevel.Debug, string loggerMemberName = "_logger", string? operationName = null)
    {
        if (!Enum.IsDefined(level) || level == LogLevel.None) throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentException.ThrowIfNullOrWhiteSpace(loggerMemberName);
        Level = level; LoggerMemberName = loggerMemberName; OperationName = operationName;
    }
    /// <summary>Entry/exit severity. Normal logger filters still apply.</summary>
    public LogLevel Level { get; }
    /// <summary>Name of an accessible ILogger field, property or parameter; defaults to _logger.</summary>
    public string LoggerMemberName { get; }
    /// <summary>Optional operation name; otherwise the containing type and method name are used.</summary>
    public string? OperationName { get; }
}
