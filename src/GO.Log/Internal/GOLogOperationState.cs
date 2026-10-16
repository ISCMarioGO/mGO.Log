using System.Collections;
using System.Diagnostics;
using GO.Log.Abstractions.Models;

namespace GO.Log.Internal;

internal sealed class GOLogOperationState(string name) : IEnumerable<KeyValuePair<string, object?>>
{
    public string Name { get; } = name;
    private readonly long _started = Stopwatch.GetTimestamp();
    public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return new("OperationName", Name);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class GOLogOperationEvent(GOLogOperationState operation, GOLogEventKind kind)
    : IEnumerable<KeyValuePair<string, object?>>
{
    public GOLogEventKind Kind { get; } = kind;
    public string Message => Kind == GOLogEventKind.Enter ? $"Entering {operation.Name}" : $"Leaving {operation.Name}";
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return new("OperationName", operation.Name);
        yield return new("{OriginalFormat}", Kind == GOLogEventKind.Enter ? "Entering {OperationName}" : "Leaving {OperationName}");
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
