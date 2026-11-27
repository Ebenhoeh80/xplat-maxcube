using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Application.Abstractions;

/// <summary>Im Anlernmodus gefundenes Gerät (N:-Antwort, NewDeviceSerializer.java:29-49).</summary>
public sealed record FoundDevice(
    DeviceType Type,
    int NumericType,
    RadioAddress Address,
    string SerialNumber);

/// <summary>Funkauslastung aus der S:-Antwort nach jedem Radiogramm (DeviceSendCommand.java:34-49).</summary>
public sealed record AirtimeStatus(int DutyCycle, int FreeMemorySlots);

/// <summary>Raumweite Temperaturkonfiguration (Frame 0x11).</summary>
public sealed record RoomConfigurationValues(
    Temperature Comfort,
    Temperature Eco,
    Temperature Maximum,
    Temperature Minimum,
    double Offset,
    Temperature WindowOpen,
    int WindowOpenMinutes);

/// <summary>Boost, Entkalkung und Ventil (Frame 0x12).</summary>
public sealed record ValveFunctionValues(
    int BoostMinutes,
    int BoostValveAngle,
    MaxCube.Domain.Enums.DayOfWeek DecalcificationDay,
    int DecalcificationHour,
    int MaxValveRaw,
    int ValveOffsetRaw);

/// <summary>
/// Erweiterte, schreibende und konfigurierende Funktionen des Cubes: Konfiguration,
/// Anlernen, Zuordnung, Strukturpflege, Zeit und Taster. Bewusst getrennt von
/// <see cref="ICubeGateway"/>, damit die Kernschnittstelle klein und leicht zu ersetzen bleibt.
/// Jede Methode läuft über dieselbe Queue und denselben Worker wie die Kernbefehle.
/// </summary>
public interface ICubeAdminGateway
{
    /// <summary>Wird nach jedem erfolgreich gesendeten Radiogramm ausgelöst.</summary>
    event EventHandler<AirtimeStatus>? AirtimeChanged;

    Task SetRoomConfigurationAsync(
        int roomId, RadioAddress master, RoomConfigurationValues values, CancellationToken cancellationToken = default);

    Task SetValveFunctionsAsync(
        int roomId, RadioAddress master, ValveFunctionValues values, CancellationToken cancellationToken = default);

    Task SetDisplayModeAsync(
        RadioAddress wallThermostat, bool showActualTemperature, CancellationToken cancellationToken = default);

    /// <summary>Ein Anlernfenster; null, wenn kein Gerät erschien (der Modus endet dann).</summary>
    Task<FoundDevice?> SearchNewDeviceAsync(TimeSpan window, CancellationToken cancellationToken = default);

    Task EndInclusionAsync(CancellationToken cancellationToken = default);

    Task SetGroupAddressAsync(RadioAddress device, int groupId, CancellationToken cancellationToken = default);

    Task RemoveGroupAddressAsync(RadioAddress device, int roomId, CancellationToken cancellationToken = default);

    Task SetLinkPartnerAsync(
        RadioAddress device, RadioAddress partner, int partnerNumericType, bool add,
        CancellationToken cancellationToken = default);

    /// <summary>Java-Gruppenkopf-Variante für Anlernen/Entfernen im Raum (`0x20`/`0x21`).</summary>
    Task SetLinkPartnerInRoomAsync(
        RadioAddress master, int roomId, RadioAddress partner, int partnerNumericType, bool add,
        CancellationToken cancellationToken = default);

    Task DeleteDevicesAsync(
        IReadOnlyList<RadioAddress> devices, bool factoryReset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schreibt Räume und Geräte des Aggregats als Metadatenblöcke. Vor dem Senden muss
    /// die Kodierung des unveränderten Zustands die empfangenen Bytes reproduzieren.
    /// </summary>
    Task WriteMetadataAsync(Cube cube, CancellationToken cancellationToken = default);

    Task<PushButtonConfiguration> ReadPushButtonAsync(
        RadioAddress cubeAddress, CancellationToken cancellationToken = default);

    Task SetPushButtonAsync(
        RadioAddress cubeAddress, PushButtonConfiguration configuration, CancellationToken cancellationToken = default);

    Task<TimeZoneConfiguration> ReadTimeZoneAsync(
        RadioAddress cubeAddress, CancellationToken cancellationToken = default);

    /// <summary>Setzt die Zeitzone; mit <paramref name="setTimeUtc"/> zusätzlich die Cube-Zeit.</summary>
    Task SetTimeZoneAsync(
        RadioAddress cubeAddress,
        TimeZoneConfiguration configuration,
        DateTimeOffset? setTimeUtc,
        CancellationToken cancellationToken = default);

    Task<string> QueryNtpServersAsync(CancellationToken cancellationToken = default);

    Task SetNtpServersAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Benutzerinitiierter Wake-up (`z:`) für einen Raum. Kein automatisches Voranschalten
    /// vor anderen Schreibbefehlen; das <c>A:</c> quittiert nur die Annahme durch den Cube.
    /// </summary>
    Task WakeUpRoomAsync(int roomId, int durationMinutes = 30, CancellationToken cancellationToken = default);

    /// <summary>Benutzerinitiierter Wake-up (`z:`) für ein einzelnes Gerät.</summary>
    Task WakeUpDeviceAsync(RadioAddress device, int durationMinutes = 30, CancellationToken cancellationToken = default);
}

/// <summary>Netzwerkkonfiguration über UDP (benutzerinitiiert; kein Broadcast beim Start).</summary>
public interface ICubeNetworkConfigurator
{
    Task<CubeNetworkConfiguration> ReadAsync(
        string serialNumber, string ipAddress, CancellationToken cancellationToken = default);

    Task WriteAsync(
        string serialNumber, string ipAddress, CubeNetworkConfiguration configuration,
        CancellationToken cancellationToken = default);
}
