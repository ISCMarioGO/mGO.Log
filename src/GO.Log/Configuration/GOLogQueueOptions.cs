namespace GO.Log.Configuration;

/// <summary>Behavior when pending capacity is exhausted.</summary>
public enum GOLogQueueFullMode
{
    /// <summary>Rejects the incoming event.</summary>
    RejectNew,
    /// <summary>Removes the oldest pending event and accepts the incoming event.</summary>
    DropOldest
}

/// <summary>Bounded queue configuration.</summary>
public sealed class GOLogQueueOptions
{
    /// <summary>Maximum pending events, excluding the batch being processed.</summary>
    public int Capacity { get; set; } = 4096;
    /// <summary>Maximum events per sink call.</summary>
    public int BatchSize { get; set; } = 64;
    /// <summary>Overflow policy.</summary>
    public GOLogQueueFullMode FullMode { get; set; } = GOLogQueueFullMode.RejectNew;
    /// <summary>Grace period before processor cancellation.</summary>
    public int ShutdownTimeoutSeconds { get; set; } = 10;
}
