using MaxCube.Application.Models;

namespace MaxCube.Application.Abstractions;

/// <summary>Einfache Uhr; erlaubt deterministische Tests ohne Task.Delay.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    /// <summary>
    /// Ortszeit mit Offset: Der Cube kodiert Temporary-Endzeiten ohne Zeitzone
    /// (RadiogramSpecification.java:187-191 nutzt Calendar.getInstance()). UTC würde
    /// in MEZ/MESZ eine falsche Endzeit senden.
    /// </summary>
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>
/// SQLite-Cache und Einstellungen. Der Cube bleibt Source of Truth; dieser Store ist
/// nur für Offline-Anzeige und Konfiguration zuständig (target-architecture.md:15,179).
/// </summary>
public interface ICubeStateStore
{
    Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default);

    Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);

    Task AppendAuditAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Letzte Audit-Einträge für die Statusansicht (chronologisch).</summary>
    Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default);
}
