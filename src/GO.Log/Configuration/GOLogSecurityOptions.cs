namespace GO.Log.Configuration;

/// <summary>Limits for snapshots; free-form text is not automatically scrubbed.</summary>
public sealed class GOLogSecurityOptions
{
    /// <summary>Replaces matching structured values with ***.</summary>
    public bool MaskSensitiveData { get; set; } = true;
    /// <summary>Case-insensitive exact property names, applied recursively.</summary>
    public string[] SensitiveKeys { get; set; } = ["password", "token", "authorization", "secret", "apiKey", "connectionString"];
    /// <summary>Maximum object/array nesting depth.</summary>
    public int MaxPropertyDepth { get; set; } = 5;
    /// <summary>Maximum characters per captured string, message, template and exception text.</summary>
    public int MaxMessageLength { get; set; } = 16384;
    /// <summary>Maximum properties/items per scope, object, array and event state.</summary>
    public int MaxProperties { get; set; } = 64;
}
