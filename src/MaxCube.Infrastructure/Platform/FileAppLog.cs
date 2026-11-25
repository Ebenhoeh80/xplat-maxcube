using System.Globalization;
using System.IO.Compression;
using MaxCube.Application.Abstractions;

namespace MaxCube.Infrastructure.Platform;

/// <summary>
/// Tageslogdatei <c>maxcube-JJJJMMTT.log</c> mit Aufbewahrung (ApplicationSettings.java:7,67-78).
/// Schreibfehler dürfen die App nie beeinträchtigen: das Protokoll ist Diagnose, keine Funktion.
/// Der Export ist rein lokal und wird ausschließlich vom Benutzer ausgelöst; es werden keine
/// Funknutzlasten, Zugangsdaten oder Tokens protokolliert.
/// </summary>
public sealed class FileAppLog : ILogStore
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

    /// <summary>Ausgeschaltet oder nur für den aktuellen Prozess; nie eine Netzwerkfunktion.</summary>
    public bool IsEnabled { get; set; } = true;

    public string Directory => _directory;

    public void Write(string level, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var now = _time.GetLocalNow();
            var file = Path.Combine(
                _directory, Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);
            // 0.6.1: Thread-Kontext als Support-Hinweis. „Call from invalid thread" war
            // im Kundenprotokoll nicht zuordenbar, solange nur Uhrzeit und Level standen.
            var context = SynchronizationContext.Current is null ? string.Empty : ",UI";
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message.ReplaceLineEndings(" ")}"
                + $" (T{Environment.CurrentManagedThreadId}{context}){Environment.NewLine}";
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
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
        if (retentionDays < 1 || !System.IO.Directory.Exists(_directory))
        {
            return 0;
        }

        var limit = _time.GetLocalNow().Date.AddDays(-retentionDays);
        var removed = 0;
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, Prefix + "*" + Extension))
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

    public IReadOnlyList<string> ReadRecent(int maxEntries)
    {
        if (maxEntries <= 0 || !System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var newest = System.IO.Directory
            .EnumerateFiles(_directory, Prefix + "*" + Extension)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null)
        {
            return [];
        }

        try
        {
            lock (_gate)
            {
                return File.ReadAllLines(newest).TakeLast(maxEntries).ToArray();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"Log konnte nicht gelesen werden: {exception.Message}"];
        }
    }

    public string Export(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        System.IO.Directory.CreateDirectory(targetDirectory);
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archive = Path.Combine(targetDirectory, $"log-export-{stamp}.zip");
        lock (_gate)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            if (System.IO.Directory.Exists(_directory))
            {
                foreach (var file in System.IO.Directory
                             .EnumerateFiles(_directory, Prefix + "*" + Extension)
                             .OrderBy(name => name, StringComparer.Ordinal))
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                }
            }
        }

        return archive;
    }
}
