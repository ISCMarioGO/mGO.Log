namespace GO.Log.Configuration;

/// <summary>Output format.</summary>
public enum GOLogFileFormat
{
    /// <summary>Readable text.</summary>
    Text,
    /// <summary>One JSON object per line.</summary>
    Json
}

/// <summary>Single-process rotating file destination settings.</summary>
public sealed class GOLogFileOptions
{
    /// <summary>Enables the built-in file destination.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Output directory, relative to the process working directory or absolute.</summary>
    public string Directory { get; set; } = "logs";
    /// <summary>Filename template with required {date}, {sequence} and optional {application} tokens.</summary>
    public string FileNamePattern { get; set; } = "{application}-{date}-{sequence}.log";
    /// <summary>Resumes the latest compatible owned file on startup. False creates a new file on the first event.</summary>
    public bool AppendOnStartup { get; set; }
    /// <summary>Text or JSON Lines.</summary>
    public GOLogFileFormat Format { get; set; } = GOLogFileFormat.Json;
    /// <summary>Layout settings used when Format is Text.</summary>
    public GOLogTextOptions Text { get; set; } = new();
    /// <summary>Rotation threshold in MiB. A single oversized event gets a file of its own.</summary>
    public double MaxFileSizeMB { get; set; } = 10;
    /// <summary>UTC last-write age limit; active file is excluded.</summary>
    public int RetentionDays { get; set; } = 7;
    /// <summary>Maximum file count, including the active file.</summary>
    public int MaxRetainedFiles { get; set; } = 30;
    /// <summary>Maximum idle interval between buffer flushes.</summary>
    public int FlushIntervalSeconds { get; set; } = 1;
}
