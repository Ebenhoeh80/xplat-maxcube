namespace MaxCube.Application.Abstractions;

/// <summary>
/// Sprachneutrale Herkunft eines Audit-Eintrags. Der Tag bleibt im Rohtext erhalten,
/// damit Benutzeraktionen, Verbindungswechsel und Reconnects unterscheidbar sind
/// (docs/SECURITY.md:87). Bewusst kein lokalisierter Text: Audit ist historisches Rohdatum.
/// </summary>
public enum AuditSource
{
    User,
    Session,
    Reconnect,
    System,
}
