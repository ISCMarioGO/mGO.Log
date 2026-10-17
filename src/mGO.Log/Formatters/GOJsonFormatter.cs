using System.Text.Json;
using System.Text.Json.Serialization;
using mGO.Log.Abstractions.Interfaces;
using mGO.Log.Abstractions.Models;

namespace mGO.Log.Formatters;

/// <summary>Serializes one event into a single JSON object with escaped newlines.</summary>
public sealed class GOJsonFormatter : IGOLogFormatter
{
    private readonly JsonSerializerOptions _options = new() { Converters = { new JsonStringEnumConverter() } };
    /// <inheritdoc />
    public string Format(GOLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return JsonSerializer.Serialize(entry, _options);
    }
}
