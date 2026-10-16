using System.Text.Json;
using System.Text.Json.Serialization;
using GO.Log.Abstractions.Interfaces;
using GO.Log.Abstractions.Models;

namespace GO.Log.Formatters;

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
