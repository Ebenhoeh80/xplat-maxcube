using System.Globalization;
using MaxCube.Application.Abstractions;

namespace MaxCube.Infrastructure.Platform;

/// <summary>
/// Tageslogdatei <c>maxcube-JJJJMMTT.log</c> mit Aufbewahrung (ApplicationSettings.java:7,67-78).
/// Schreibfehler dürfen die App nie beeinträchtigen: das Protokoll ist Diagnose, keine Funktion.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private const string Prefix = "maxcube-";
    private const string Extension = ".log";

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public FileAppLog(string directory, TimeProvider? time = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _time = time ?? TimeProvider.System;
    }

    public void Write(string level, string message)
    {
        try
        {
            var now = _time.GetLocalNow();
            var file = Path.Combine(
                _directory, Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message.ReplaceLineEndings(" ")}{Environment.NewLine}";
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(file, line);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ein volles oder gesperrtes Laufwerk darf die Anwendung nicht stören.
        }
    }

    public int Cleanup(int retentionDays)
    {
        if (retentionDays < 1 || !Directory.Exists(_directory))
        {
            return 0;
        }

        var limit = _time.GetLocalNow().Date.AddDays(-retentionDays);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_directory, Prefix + "*" + Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (!DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= limit)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Eine gesperrte Datei bleibt bis zum nächsten Start stehen.
            }
        }

        return removed;
    }
}
