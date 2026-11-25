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

/// <summary>Verwirft alle Einträge; für Tests und die Designzeit.</summary>
public sealed class NullAppLog : IAppLog
{
    public void Write(string level, string message)
    {
    }

    public int Cleanup(int retentionDays) => 0;
}
