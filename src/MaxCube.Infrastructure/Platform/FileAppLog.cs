using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
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
    private const string ConsolidatedName = "maxcube-export.log";

    private static readonly Regex LevelMarker = new(@"\[(?<level>[A-Za-z]+)\]", RegexOptions.Compiled);
    private static readonly Regex IPv4Address = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\b",
        RegexOptions.Compiled);
    private static readonly Regex SerialNumber = new(@"\bJEQ[A-Za-z0-9]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>Default-Export: alle bekannten Stufen, keine Redaktion – wie bisher.</summary>
    public string Export(string targetDirectory) => Export(targetDirectory, new LogExportOptions());

    public string Export(string targetDirectory, LogExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(options);

        System.IO.Directory.CreateDirectory(targetDirectory);
        var minRank = Rank(options.MinLevel);
        var exported = new List<string>();
        var sources = new List<string>();

        lock (_gate)
        {
            if (System.IO.Directory.Exists(_directory))
            {
                sources.AddRange(System.IO.Directory
                    .EnumerateFiles(_directory, Prefix + "*" + Extension)
                    .OrderBy(name => name, StringComparer.Ordinal));
                foreach (var file in sources)
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        var match = LevelMarker.Match(line);
                        if (!match.Success)
                        {
                            continue;
                        }

                        var rank = Rank(match.Groups["level"].Value);
                        if (rank is null || rank.Value < minRank)
                        {
                            continue;
                        }

                        exported.Add(options.RedactPersonalData ? Redact(line, options.KnownNames) : line);
                    }
                }
            }
        }

        // Ein leeres ZIP sieht nach einem erfolgreichen Export aus und ist irreführend.
        // Die UI behandelt einen leeren Pfad bereits als „nichts exportiert“.
        if (exported.Count == 0)
        {
            return string.Empty;
        }

        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archive = Path.Combine(targetDirectory, $"log-export-{stamp}.zip");
        var entryName = sources.Count == 1 ? Path.GetFileName(sources[0]) : ConsolidatedName;

        lock (_gate)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            var entry = zip.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            foreach (var line in exported)
            {
                writer.WriteLine(line);
            }
        }

        return archive;
    }

    private static int Rank(LogExportLevel level) => level switch
    {
        LogExportLevel.Info => 0,
        LogExportLevel.Warn => 1,
        LogExportLevel.Error => 2,
        _ => 0,
    };

    private static int? Rank(string level) => level.Trim().ToUpperInvariant() switch
    {
        "INFO" => 0,
        "WARN" => 1,
        "ERROR" => 2,
        _ => null,
    };

    private static string Redact(string line, IReadOnlyList<string>? knownNames)
    {
        var redacted = IPv4Address.Replace(line, "[IP]");
        redacted = SerialNumber.Replace(redacted, "[SERIAL]");

        foreach (var name in (knownNames ?? Array.Empty<string>())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .OrderByDescending(name => name.Length)
                     .ThenBy(name => name, StringComparer.Ordinal))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])";
            redacted = Regex.Replace(redacted, pattern, "[NAME]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return redacted;
    }
}
