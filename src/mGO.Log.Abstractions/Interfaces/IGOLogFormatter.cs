using mGO.Log.Abstractions.Models;

namespace mGO.Log.Abstractions.Interfaces;

/// <summary>Formats a snapshot without a trailing line separator.</summary>
public interface IGOLogFormatter
{
    /// <summary>Formats one event. JSON formatters must return a single valid JSON object.</summary>
    string Format(GOLogEntry entry);
}
