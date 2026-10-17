using Microsoft.Extensions.Logging;
using mGO.Log.Exceptions;

namespace mGO.Log.Configuration;

/// <summary>Configuration captured at engine creation; changes require a new engine.</summary>
public sealed class GOLogOptions
{
    /// <summary>Enables event capture.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Application identity included in events and file names.</summary>
    public string ApplicationName { get; set; } = "Application";
    /// <summary>Lowest enabled severity. None disables all logging.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    /// <summary>Captures external scopes.</summary>
    public bool IncludeScopes { get; set; } = true;
    /// <summary>Captures Activity.Current W3C identifiers.</summary>
    public bool IncludeActivityTracking { get; set; } = true;
    /// <summary>Captures process and producer-thread identity without walking the stack.</summary>
    public bool IncludeExecutionContext { get; set; } = true;
    /// <summary>File destination settings.</summary>
    public GOLogFileOptions File { get; set; } = new();
    /// <summary>Optional console destination settings.</summary>
    public GOLogConsoleOptions Console { get; set; } = new();
    /// <summary>Queue settings.</summary>
    public GOLogQueueOptions Queue { get; set; } = new();
    /// <summary>Capture limits and redaction settings.</summary>
    public GOLogSecurityOptions Security { get; set; } = new();

    /// <summary>Throws a configuration exception for invalid values.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationName) || !Enum.IsDefined(MinimumLevel) ||
            File is null || Console is null || Queue is null || Security is null)
            throw new GOLogConfigurationException("ApplicationName, MinimumLevel and option groups must be valid.");
        if (Queue.Capacity < 1 || Queue.BatchSize < 1 || Queue.ShutdownTimeoutSeconds <= 0 ||
            Queue.ShutdownTimeoutSeconds > 86400 || !Enum.IsDefined(Queue.FullMode))
            throw new GOLogConfigurationException("Queue capacity/batch must be positive and shutdown timeout must be 1..86400 seconds.");
        if (string.IsNullOrWhiteSpace(File.Directory) || string.IsNullOrWhiteSpace(File.FileNamePattern) ||
            File.FileNamePattern.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !File.FileNamePattern.Contains("{date}", StringComparison.Ordinal) ||
            !File.FileNamePattern.Contains("{sequence}", StringComparison.Ordinal) ||
            !Enum.IsDefined(File.Format) || !double.IsFinite(File.MaxFileSizeMB) ||
            File.MaxFileSizeMB <= 0 || File.MaxFileSizeMB > 1_000_000 ||
            File.RetentionDays < 1 || File.RetentionDays > 36500 || File.MaxRetainedFiles < 1 ||
            File.FlushIntervalSeconds < 1 || File.FlushIntervalSeconds > 86400)
            throw new GOLogConfigurationException("Invalid file settings; pattern requires {date} and {sequence} and must be a filename.");
        var pattern = File.FileNamePattern.Replace("{date}", "").Replace("{sequence}", "").Replace("{application}", "");
        if (pattern.Contains('{') || pattern.Contains('}') ||
            Security.MaxPropertyDepth is < 1 or > 32 || Security.MaxMessageLength < 1 ||
            Security.MaxProperties < 1 || Security.SensitiveKeys is null || Security.SensitiveKeys.Any(string.IsNullOrWhiteSpace))
            throw new GOLogConfigurationException("Invalid pattern tokens or security limits.");
        if (!Enum.IsDefined(Console.StandardErrorFromLevel))
            throw new GOLogConfigurationException("Console.StandardErrorFromLevel must be a defined log level.");
        ValidateText(File.Text);
        ValidateText(Console.Text);
    }

    internal static void ValidateText(GOLogTextOptions? text)
    {
        if (text is null || !Enum.IsDefined(text.Style) || string.IsNullOrWhiteSpace(text.TimestampFormat) ||
            text.TimestampFormat.Length > 128 || text.TimestampFormat.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            throw new GOLogConfigurationException("Text style and timestamp format must be valid, without control characters.");
        try { _ = DateTimeOffset.UtcNow.ToString(text.TimestampFormat, System.Globalization.CultureInfo.InvariantCulture); }
        catch (FormatException) { throw new GOLogConfigurationException("Invalid text timestamp format."); }
    }
}
