namespace GO.Log.Abstractions.Models;

/// <summary>Semantic direction of an event, independent of severity.</summary>
public enum GOLogEventKind
{
    /// <summary>A regular application message.</summary>
    Message,
    /// <summary>Entering an explicitly instrumented operation.</summary>
    Enter,
    /// <summary>Leaving an operation, without implying success.</summary>
    Leave
}
