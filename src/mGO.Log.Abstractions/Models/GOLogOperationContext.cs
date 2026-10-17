namespace mGO.Log.Abstractions.Models;

/// <summary>Immutable logical-operation timing snapshot, using a monotonic clock.</summary>
public sealed class GOLogOperationContext
{
    /// <summary>Creates an operation snapshot; root depth is zero and indentation is capped at 64.</summary>
    public GOLogOperationContext(string name, int depth, double elapsedMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 64);
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        Name = name; Depth = depth; ElapsedMilliseconds = elapsedMilliseconds;
    }
    /// <summary>Operation name.</summary>
    public string Name { get; }
    /// <summary>Logical nesting depth, capped at 64 for display safety.</summary>
    public int Depth { get; }
    /// <summary>Elapsed time since entry, measured using Stopwatch rather than wall-clock time.</summary>
    public double ElapsedMilliseconds { get; }
}
