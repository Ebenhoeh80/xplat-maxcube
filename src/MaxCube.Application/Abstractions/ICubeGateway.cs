using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Application.Abstractions;

/// <summary>Verbindungsstatus des physischen Cubes.</summary>
public enum CubeConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted,
}

/// <summary>Verbindungsdaten; Port 62910 ist der physische Cube (54321 nur virtuell).</summary>
public sealed record CubeEndpoint(string Host, int Port);

/// <summary>
/// Fachliches Tor zum Cube. Die Implementierung liegt in Infrastructure und nutzt dort
/// die Protokollschicht; Application selbst kennt weder TCP noch SQLite (target-architecture.md:85).
/// </summary>
public interface ICubeGateway
{
    CubeConnectionState State { get; }

    event EventHandler<CubeConnectionState>? StateChanged;

    /// <summary>Neue Gerätewerte aus einem l:-Refresh (Live-Zustand).</summary>
    event EventHandler<Cube>? CubeRefreshed;

    /// <summary>Die Sitzung gilt nach wiederholten Fehlern als nicht erreichbar.</summary>
    event EventHandler? SessionUnreachable;

    /// <summary>Verbindet, liest H:/C:/M:/L: und liefert das frische Aggregat.</summary>
    Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default);

    Task SetRoomTemperatureAsync(
        int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default);

    Task SetRoomControlModeAsync(
        int roomId,
        RadioAddress master,
        RoomControlMode mode,
        Temperature? setPoint,
        DateTimeOffset? until,
        CancellationToken cancellationToken = default);

    /// <summary>Sendet die ein oder zwei 0x10-Telegramme eines Tagesprofils.</summary>
    Task SetWeekDayProfileAsync(
        int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
