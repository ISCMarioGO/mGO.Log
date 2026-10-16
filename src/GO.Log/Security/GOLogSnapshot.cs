using System.Collections;
using System.Reflection;
using System.Text.Json;
using GO.Log.Configuration;

namespace GO.Log.Security;

internal sealed class GOLogSnapshot(GOLogSecurityOptions options)
{
    private readonly HashSet<string> _keys = new(options.SensitiveKeys, StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _secrets = [];
    public string Limit(string value) => value.Length <= options.MaxMessageLength ? value : value[..options.MaxMessageLength];
    public string Redact(string value)
    {
        foreach (var secret in _secrets.Distinct().OrderByDescending(x => x.Length))
            value = value.Replace(secret, "***", StringComparison.Ordinal);
        return Limit(value);
    }
    public JsonElement Capture(object? value) => JsonSerializer.SerializeToElement(Copy(value, 0));
    public JsonElement CaptureProperty(string key, object? value) => JsonSerializer.SerializeToElement(Property(key, value, 0));
    public Dictionary<string, JsonElement> Properties(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var pair in values.Take(options.MaxProperties))
        {
            if (pair.Key == "{OriginalFormat}") continue;
            result[Limit(pair.Key)] = JsonSerializer.SerializeToElement(Property(pair.Key, pair.Value, 0));
        }
        return result;
    }
    private object? Property(string key, object? value, int depth)
    {
        if (options.MaskSensitiveData && _keys.Contains(key))
        {
            // Remember bounded scalar strings to scrub template-rendered values as well.
            if (value is string text && text.Length > 0) _secrets.Add(text);
            else if (value is JsonElement { ValueKind: JsonValueKind.String } json && json.GetString() is { Length: > 0 } s)
                _secrets.Add(s);
            return "***";
        }
        return Copy(value, depth);
    }
    private object? Copy(object? value, int depth)
    {
        if (value is null) return null;
        if (value is string text) return Limit(text);
        if (depth >= options.MaxPropertyDepth) return "[MaxDepth]";
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.Object => json.EnumerateObject().Take(options.MaxProperties)
                    .ToDictionary(p => Limit(p.Name), p => Property(p.Name, p.Value, depth + 1)),
                JsonValueKind.Array => json.EnumerateArray().Take(options.MaxProperties).Select(x => Copy(x, depth + 1)).ToArray(),
                JsonValueKind.String => Limit(json.GetString() ?? ""),
                JsonValueKind.Undefined or JsonValueKind.Null => null,
                _ => json.Clone()
            };
        }
        if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            var result = new Dictionary<string, object?>();
            foreach (var pair in pairs.Take(options.MaxProperties))
                result[Limit(pair.Key)] = Property(pair.Key, pair.Value, depth + 1);
            return result;
        }
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (result.Count >= options.MaxProperties) break;
                var key = Limit(entry.Key?.ToString() ?? "null");
                result[key] = Property(key, entry.Value, depth + 1);
            }
            return result;
        }
        if (value is IEnumerable items)
        {
            var result = new List<object?>();
            foreach (var item in items)
            {
                if (result.Count >= options.MaxProperties) break;
                result.Add(Copy(item, depth + 1));
            }
            return result;
        }
        var type = value.GetType();
        if (type.IsPrimitive || value is decimal or DateTime or DateTimeOffset or Guid or TimeSpan || type.IsEnum)
            return value;
        var properties = new Dictionary<string, object?>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Take(options.MaxProperties))
            properties[Limit(property.Name)] = Property(property.Name, property.GetValue(value), depth + 1);
        return properties;
    }
}
