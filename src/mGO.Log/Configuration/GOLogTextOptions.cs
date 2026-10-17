namespace mGO.Log.Configuration;

/// <summary>Built-in layouts for human-readable logs.</summary>
public enum GOLogTextStyle
{
    /// <summary>Original detailed layout including structured context.</summary>
    Standard,
    /// <summary>Short severity/category/message layout.</summary>
    Compact,
    /// <summary>Aligned process/thread, elapsed time and operation direction columns.</summary>
    Trace
}

/// <summary>Text presentation settings, independent of the structured message template.</summary>
public sealed class GOLogTextOptions
{
    /// <summary>Presentation preset. Standard preserves the original text layout.</summary>
    public GOLogTextStyle Style { get; set; } = GOLogTextStyle.Standard;
    /// <summary>DateTimeOffset format, evaluated in UTC with invariant culture.</summary>
    public string TimestampFormat { get; set; } = "O";
    /// <summary>Appends structured properties.</summary>
    public bool IncludeProperties { get; set; } = true;
    /// <summary>Appends captured scopes.</summary>
    public bool IncludeScopes { get; set; } = true;
    /// <summary>Appends full captured exception text; otherwise appends its type and message.</summary>
    public bool IncludeExceptionDetails { get; set; } = true;
}
