namespace GO.Log.Abstractions.Models;

/// <summary>Immutable process and producer-thread identity captured during the log call.</summary>
public sealed class GOLogExecutionContext
{
    /// <summary>Creates producer context; thread identity is not a request correlation identifier.</summary>
    public GOLogExecutionContext(int processId, string processName, int threadId, string? threadName)
    {
        ArgumentNullException.ThrowIfNull(processName);
        ProcessId = processId; ProcessName = processName; ThreadId = threadId; ThreadName = threadName;
    }
    /// <summary>Operating system process identifier.</summary>
    public int ProcessId { get; }
    /// <summary>Process name captured when the provider was created.</summary>
    public string ProcessName { get; }
    /// <summary>Managed identifier of the thread executing ILogger.Log.</summary>
    public int ThreadId { get; }
    /// <summary>Optional producer-thread name.</summary>
    public string? ThreadName { get; }
}
