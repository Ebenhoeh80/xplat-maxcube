namespace MaxCube.Application.Abstractions;

/// <summary>
/// Liefert die App-Version aus genau einer Quelle (Assembly-Informationsversion).
/// Die Implementierung liegt in der UI-Schicht; Application kennt keine Assembly-Details.
/// </summary>
public interface IAppVersionProvider
{
    string Version { get; }
}
