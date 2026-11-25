namespace MaxCube.Application.Abstractions;

/// <summary>Hardwarefreie Abstraktion über den UDP-Cube-Finder (Port 23272).</summary>
public interface ICubeDiscovery
{
    Task<IReadOnlyList<Models.DiscoveredCube>> DiscoverAsync(
        TimeSpan duration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Transport unterhalb der Discovery: liefert rohe Antwort-Datagramme. Die echte
/// Implementierung nutzt UDP; Tests geben gespeicherte Bytes zurück (kein Broadcast).
/// </summary>
public interface IDiscoveryTransport
{
    Task<IReadOnlyList<Models.DiscoveryDatagram>> ProbeAsync(
        byte[] request, TimeSpan duration, CancellationToken cancellationToken = default);
}
