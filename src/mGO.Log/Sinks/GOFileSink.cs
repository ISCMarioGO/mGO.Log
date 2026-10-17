using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using mGO.Log.Abstractions.Interfaces;
using mGO.Log.Abstractions.Models;
using mGO.Log.Configuration;
using mGO.Log.Formatters;
using mGO.Log.Internal;

namespace mGO.Log.Sinks;

/// <summary>
/// Serial-use file sink. Creates or resumes files on startup and retains only matching files with ownership sidecars.
/// One writer per directory/application/pattern is enforced with a lease; multiprocess shared logging is unsupported.
/// </summary>
public sealed class GOFileSink : IGOLogSink
{
    private readonly GOLogOptions _options;
    private readonly IGOLogFormatter _formatter;
    private readonly TimeProvider _clock;
    private readonly string _directory, _application, _owner, _formatMarker;
    private readonly Regex _managedName;
    private FileStream? _stream, _lease;
    private string? _activePath;
    private DateOnly _day;
    private long _size, _sequence;
    private bool _disposed;
    private bool _startupChecked;

    /// <summary>Creates a lazy file destination. A custom clock supports deterministic rotation tests.</summary>
    public GOFileSink(GOLogOptions options, IGOLogFormatter? formatter = null, TimeProvider? timeProvider = null)
    {
        _options = OptionsSnapshot.Copy(options);
        _formatter = formatter ?? (_options.File.Format == GOLogFileFormat.Json ? new GOJsonFormatter() : new GOTextFormatter(_options.File.Text));
        _clock = timeProvider ?? TimeProvider.System;
        _directory = Path.GetFullPath(_options.File.Directory);
        _application = new string(_options.ApplicationName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        _owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_application + "|" + _options.File.FileNamePattern)));
        _formatMarker = _owner + "|" + _options.File.Format;
        var pattern = Regex.Escape(_options.File.FileNamePattern.Replace("{application}", _application))
            .Replace(Regex.Escape("{date}"), "(?<date>[0-9]{8})")
            .Replace(Regex.Escape("{sequence}"), "(?<sequence>[0-9]{6,19})");
        _managedName = new Regex("\\A" + pattern + "\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    /// <inheritdoc />
    public async ValueTask WriteBatchAsync(IReadOnlyList<GOLogEntry> entries, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Encoding.UTF8.GetBytes(_formatter.Format(entry) + "\n");
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            if (!_startupChecked)
            {
                AcquireLease();
                _startupChecked = true;
                if (_options.File.AppendOnStartup)
                    await TryResumeAsync(today, bytes.LongLength, cancellationToken).ConfigureAwait(false);
            }
            if (_stream is null || _day != today || (_size > 0 && _size + bytes.LongLength > _options.File.MaxFileSizeMB * 1024 * 1024))
                await RotateAsync(today, cancellationToken).ConfigureAwait(false);
            await _stream!.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            _size += bytes.LongLength;
        }
    }
    private async Task RotateAsync(DateOnly day, CancellationToken token)
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false); _stream = null;
        }
        AcquireLease();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var name = _options.File.FileNamePattern.Replace("{application}", _application)
                .Replace("{date}", day.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                .Replace("{sequence}", (_sequence++).ToString("D6", CultureInfo.InvariantCulture));
            var path = Path.Combine(_directory, name);
            if (File.Exists(path) || File.Exists(path + ".golog-managed")) continue;
            try
            {
                _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (IOException) when (File.Exists(path)) { continue; }
            _activePath = path; _day = day; _size = 0;
            // A failed marker write leaves the file unmanaged, never eligible for deletion.
            await File.WriteAllTextAsync(path + ".golog-managed", _formatMarker, token).ConfigureAwait(false);
            Retain();
            break;
        }
    }
    private void AcquireLease()
    {
        System.IO.Directory.CreateDirectory(_directory);
        _lease ??= new FileStream(Path.Combine(_directory, $".golog-{_owner}.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }
    private async Task TryResumeAsync(DateOnly day, long nextEntrySize, CancellationToken token)
    {
        var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var candidate = new DirectoryInfo(_directory).EnumerateFiles()
            .Select(file => (File: file, Match: _managedName.Match(file.Name)))
            .Where(item => item.Match.Success && item.Match.Groups["date"].Value == date &&
                !item.File.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                ReadOwnerMarker(item.File.FullName) == _formatMarker)
            .OrderByDescending(item => item.File.LastWriteTimeUtc)
            .ThenByDescending(item => item.Match.Groups["sequence"].Value.Length)
            .ThenByDescending(item => item.Match.Groups["sequence"].Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate.File is null || !long.TryParse(candidate.Match.Groups["sequence"].Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence == long.MaxValue)
            return;
        // Open never creates or truncates a file that disappeared after enumeration.
        FileStream? stream = new(candidate.File.FullName, FileMode.Open, FileAccess.ReadWrite,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            var size = stream.Length;
            if (size > 0)
            {
                if (size + nextEntrySize > _options.File.MaxFileSizeMB * 1024 * 1024) return;
                stream.Seek(-1, SeekOrigin.End);
                var lastByte = new byte[1];
                await stream.ReadExactlyAsync(lastByte, token).ConfigureAwait(false);
                // Preserve incomplete trailing records rather than joining new JSON/text onto them.
                if (lastByte[0] != (byte)'\n') return;
            }
            stream.Seek(0, SeekOrigin.End);
            _stream = stream; stream = null;
            _activePath = candidate.File.FullName; _day = day; _size = size; _sequence = sequence + 1;
            Retain();
        }
        finally { if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false); }
    }
    private void Retain()
    {
        var files = new DirectoryInfo(_directory).EnumerateFiles()
            .Where(f => _managedName.IsMatch(f.Name) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Where(f => IsOwned(f.FullName)).OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).ToArray();
        var kept = 1; // active path is always reserved
        foreach (var file in files)
        {
            if (file.FullName == _activePath) continue;
            if (_clock.GetUtcNow().UtcDateTime - file.LastWriteTimeUtc > TimeSpan.FromDays(_options.File.RetentionDays) ||
                kept >= _options.File.MaxRetainedFiles)
            {
                file.Delete();
                File.Delete(file.FullName + ".golog-managed");
            }
            else kept++;
        }
    }
    private bool IsOwned(string path)
    {
        var marker = ReadOwnerMarker(path);
        // Legacy owner-only markers remain eligible for retention, but not for format-safe appending.
        return marker == _owner || marker == _owner + "|Text" || marker == _owner + "|Json";
    }
    private string? ReadOwnerMarker(string path)
    {
        var marker = new FileInfo(path + ".golog-managed");
        return marker.Exists && !marker.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
            (marker.Length == _owner.Length || marker.Length == _formatMarker.Length)
            ? File.ReadAllText(marker.FullName) : null;
    }
    /// <inheritdoc />
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is not null) await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false); }
        finally { if (_lease is not null) await _lease.DisposeAsync().ConfigureAwait(false); }
    }
}
