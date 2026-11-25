namespace MaxCube.Application.Abstractions;

/// <summary>
/// Lokales Anwendungsprotokoll (nur Datei, kein Versand). Ersetzt die Logdatei des
/// Originals; Zugangsdaten und Funknutzlasten gehören nie hinein.
/// </summary>
public interface IAppLog
{
    void Write(string level, string message);

    /// <summary>Löscht Logdateien, die älter als <paramref name="retentionDays"/> Tage sind.</summary>
    int Cleanup(int retentionDays);
}

/// <summary>
/// Erweiterung für die Oberfläche: ein-/ausschaltbar, lokal sichtbar und als ZIP exportierbar.
/// Alle Operationen sind rein lokal; ein Export entsteht ausschließlich auf Benutzerklick.
/// </summary>
public interface ILogStore : IAppLog
{
    bool IsEnabled { get; set; }

    string Directory { get; }

    /// <summary>Letzte Einträge der aktuellen Tagesdatei, in chronologischer Reihenfolge.</summary>
    IReadOnlyList<string> ReadRecent(int maxEntries);

    /// <summary>Schreibt alle Logdateien in ein ZIP im Zielordner und gibt den Pfad zurück.</summary>
    string Export(string targetDirectory);
}

/// <summary>Verwirft alle Einträge; für Tests und die Designzeit.</summary>
public sealed class NullAppLog : IAppLog
{
    public void Write(string level, string message)
    {
    }

    public int Cleanup(int retentionDays) => 0;
}

/// <summary>In-Memory-Variante für Tests und die Designzeit; speichert nichts dauerhaft.</summary>
public sealed class NullLogStore : ILogStore
{
    public bool IsEnabled { get; set; } = true;

    public string Directory => string.Empty;

    public List<string> Entries { get; } = [];

    public void Write(string level, string message)
    {
        if (IsEnabled)
        {
            Entries.Add($"[{level}] {message}");
        }
    }

    public int Cleanup(int retentionDays) => 0;

    public IReadOnlyList<string> ReadRecent(int maxEntries)
        => Entries.TakeLast(Math.Max(0, maxEntries)).ToArray();

    public string Export(string targetDirectory) => string.Empty;
}
