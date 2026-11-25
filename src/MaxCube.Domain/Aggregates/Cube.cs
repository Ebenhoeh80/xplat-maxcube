using MaxCube.Domain.Enums;
using MaxCube.Domain.Events;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Domain.Aggregates;

/// <summary>
/// Aggregate Root. Spiegelt MaxCubeState (MaxCubeState.java:12-33): ein House, 0..20 Räume,
/// Geräte ohne Raumzuordnung, Seriennummer/Firmware/Funkadresse aus dem H:-Handshake.
/// Der Cube bleibt Source of Truth; SQLite ist nur Cache (target-architecture.md, Abschnitt 1 A3).
/// </summary>
public sealed class Cube
{
    private readonly List<Room> _rooms = [];
    private readonly List<Device> _devices = [];
    private readonly List<DomainEvent> _events = [];

    public Cube(SerialNumber serialNumber, int firmwareVersion, RadioAddress rfAddress, bool daylightSaving)
    {
        SerialNumber = serialNumber;
        FirmwareVersion = firmwareVersion;
        RfAddress = rfAddress;
        DaylightSaving = daylightSaving;
    }

    public SerialNumber SerialNumber { get; }
    public int FirmwareVersion { get; }
    public RadioAddress RfAddress { get; }
    public bool DaylightSaving { get; private set; }
    public House House { get; } = new();
    public IReadOnlyList<Room> Rooms => _rooms;
    public IReadOnlyList<Device> Devices => _devices;
    public IReadOnlyList<DomainEvent> DomainEvents => _events;

    public Room? GetRoom(int roomId) => _rooms.FirstOrDefault(r => r.Id == roomId);

    public Device? GetDevice(RadioAddress address) => _devices.FirstOrDefault(d => d.RadioAddress == address);

    public void AddRoom(Room room)
    {
        if (_rooms.Count >= DomainLimits.MaxRoomCount)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"A cube supports at most {DomainLimits.MaxRoomCount} rooms.");
        }

        if (GetRoom(room.Id) is not null)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, $"Room {room.Id} already exists.");
        }

        _rooms.Add(room);
    }

    /// <summary>
    /// Fügt ein Gerät hinzu und ordnet es seinem Raum zu. roomId == 0 bedeutet House
    /// (MetaDataSerializer.java:69-78). Die 140er-Grenze gilt für das Gesamtsystem
    /// (max.local.properties:3), ihre Durchsetzung hier ist INFERENZ (domain-model.md:94).
    /// </summary>
    public void AddDevice(Device device)
    {
        if (_devices.Count >= DomainLimits.MaxDeviceCount)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"A cube supports at most {DomainLimits.MaxDeviceCount} devices.");
        }

        if (GetDevice(device.RadioAddress) is not null)
        {
            throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress,
                $"Device {device.RadioAddress} already exists.");
        }

        // Erst alle Prüfungen, dann die Mutation: ein abgelehntes Gerät darf nicht
        // halb verbucht in _devices liegen bleiben.
        Room? room = null;
        if (device.RoomId != House.Id)
        {
            room = GetRoom(device.RoomId)
                ?? throw new DomainValidationException(
                    DomainValidationCodes.Validation, $"Room {device.RoomId} for device {device.RadioAddress} does not exist.");
            room.AddDevice(device);
        }

        _devices.Add(device);
        if (room is null)
        {
            House.AddDevice(device);
        }
    }

    /// <summary>Raum ist die Steuereinheit (Room.java:102, domain-model.md:102).</summary>
    public void SetRoomTemperature(int roomId, Temperature setPoint)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetTemperature(setPoint);
        _events.Add(new RoomStateChanged(roomId, "SetPointTemperature"));
    }

    public void SetControlMode(int roomId, RoomControlMode mode)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetControlMode(mode);
        _events.Add(new RoomStateChanged(roomId, "ControlMode"));
    }

    public void SetTemperatureMode(int roomId, RoomTemperatureMode mode)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetTemperatureMode(mode);
        _events.Add(new RoomStateChanged(roomId, "TemperatureMode"));
    }

    /// <summary>
    /// Setzt die Ist-Temperatur eines Raums. Wird vom Cache-Roundtrip benötigt, weil ein
    /// Raum ohne Geräte sonst keine Telemetrie zugewiesen bekommt.
    /// </summary>
    public void SetRoomActualTemperature(int roomId, MeasuredTemperature actual)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetActualTemperature(actual);
        _events.Add(new RoomStateChanged(roomId, "ActualTemperature"));
    }

    public void UpdateDeviceState(RadioAddress address, DeviceStateInfo stateInfo, RadioState radioState)
    {
        var device = GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        device.SetState(stateInfo, radioState);
        _events.Add(new DeviceStateChanged(address, "State"));
    }

    /// <summary>
    /// Wendet die L:-Telemetrie an. Solltemperatur, Modus und abgeleiteter Temperaturmodus
    /// entsprechen DeviceStateSerializer.java:118-137 (Raum ist die Steuereinheit).
    /// </summary>
    public void ApplyDeviceTelemetry(
        RadioAddress address,
        DeviceStateInfo stateInfo,
        RadioState radioState,
        Temperature? setPoint = null,
        MeasuredTemperature? actual = null,
        RoomControlMode? controlMode = null,
        RoomTemperatureMode? temperatureMode = null)
    {
        var device = GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        device.SetState(stateInfo, radioState);

        var room = GetRoom(device.RoomId);
        if (room is null)
        {
            return;
        }

        if (setPoint is not null)
        {
            room.SetTemperature(setPoint.Value);
        }

        if (actual is not null)
        {
            room.SetActualTemperature(actual.Value);
        }

        if (controlMode is not null)
        {
            room.SetControlMode(controlMode.Value);
        }

        var derived = temperatureMode
            ?? (setPoint is null ? null : DeriveTemperatureMode(room, setPoint.Value));
        if (derived is not null)
        {
            room.SetTemperatureMode(derived.Value);
        }

        _events.Add(new RoomStateChanged(room.Id, "DeviceTelemetry"));
    }

    private static RoomTemperatureMode DeriveTemperatureMode(Room room, Temperature setPoint)
    {
        if (setPoint == room.EcoTemperature) return RoomTemperatureMode.Eco;
        if (setPoint == room.ComfortTemperature) return RoomTemperatureMode.Comfort;
        return RoomTemperatureMode.Normal;
    }

    /// <summary>
    /// Wendet die gerätespezifischen C:-Konfigurationsbytes an
    /// (DeviceConfigurationSerializer.java:46-71, HeatingThermostatDeviceState.java:112-255).
    /// Die Byte-Positionen sind relativ zum Ende des 18-Byte-Headers.
    /// </summary>
    public void ApplyRoomConfiguration(
        int roomId,
        Temperature comfort,
        Temperature eco,
        Temperature maximum,
        Temperature minimum,
        double offset,
        Temperature windowOpen,
        int windowOpenDuration,
        int boostDuration,
        int boostValveAngle,
        DayOfWeek decalcificationDay,
        int decalcificationHour)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetConfiguration(comfort, eco, maximum, minimum, offset, windowOpen,
            windowOpenDuration, boostDuration, boostValveAngle, decalcificationDay, decalcificationHour);
        _events.Add(new RoomStateChanged(roomId, "Configuration"));
    }

    /// <summary>
    /// Übernimmt das aus config[11..] dekodierte Wochenprofil. Getrennt von
    /// <see cref="ApplyRoomConfiguration"/>, damit ein fehlendes/fehlerhaftes Profil die
    /// übrige Konfiguration nicht verfälscht (Gremium Runde 1, M-F).
    /// </summary>
    public void ApplyWeekProfile(int roomId, WeekTemperatureProfile profile)
    {
        var room = GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
        room.SetWeekProfile(profile ?? throw new ArgumentNullException(nameof(profile)));
        _events.Add(new RoomStateChanged(roomId, "WeekProfile"));
    }

    /// <summary>
    /// Maximale Ventilöffnung/Ventil-Offset als Rohbytes aus config[9]/[10] des Cubes.
    /// </summary>
    public void ApplyValveConfiguration(int roomId, int maxValveRaw, int valveOffsetRaw)
    {
        var room = RequireRoom(roomId);
        room.SetValveConfiguration(maxValveRaw, valveOffsetRaw);
        _events.Add(new RoomStateChanged(roomId, "ValveConfiguration"));
    }

    /// <summary>Kleinste freie Raum-ID ab 1; die Metadaten speichern die ID in einem Byte.</summary>
    public int NextFreeRoomId()
    {
        for (var id = 1; id <= 255; id++)
        {
            if (GetRoom(id) is null)
            {
                return id;
            }
        }

        throw new DomainValidationException(DomainValidationCodes.Validation, "No free room id.");
    }

    /// <summary>Raum umbenennen; Namen sind case-insensitiv eindeutig (ConfigureDeviceValidator.java:123-138).</summary>
    public void RenameRoom(int roomId, string name)
    {
        var room = RequireRoom(roomId);
        var validated = Device.ValidateName(name);
        if (_rooms.Any(other => other.Id != roomId
            && string.Equals(other.Name, validated, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, $"A room named '{validated}' already exists.");
        }

        room.Rename(validated);
        _events.Add(new RoomStateChanged(roomId, "Name"));
    }

    public void RenameDevice(RadioAddress address, string name)
    {
        var device = GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        var validated = Device.ValidateName(name);
        if (_devices.Any(other => other.RadioAddress != address
            && string.Equals(other.Name, validated, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                DomainValidationCodes.DeviceNameAlreadyExists, $"A device named '{validated}' already exists.");
        }

        device.Rename(validated);
        _events.Add(new DeviceStateChanged(address, "Name"));
    }

    /// <summary>Ändert die Raumreihenfolge; <paramref name="orderedRoomIds"/> muss genau alle Räume enthalten.</summary>
    public void ReorderRooms(IReadOnlyList<int> orderedRoomIds)
    {
        ArgumentNullException.ThrowIfNull(orderedRoomIds);
        if (orderedRoomIds.Count != _rooms.Count
            || orderedRoomIds.Distinct().Count() != orderedRoomIds.Count
            || orderedRoomIds.Any(id => GetRoom(id) is null))
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "The new room order must contain every room exactly once.");
        }

        var ordered = orderedRoomIds.Select(id => GetRoom(id)!).ToList();
        _rooms.Clear();
        _rooms.AddRange(ordered);
    }

    /// <summary>
    /// Entfernt ein Gerät. War es das Master-Thermostat, wird das nächste Heizkörperthermostat
    /// Master; ein Raum ohne Thermostat behält seine alte Master-Adresse nicht (leerer Master).
    /// </summary>
    public void RemoveDevice(RadioAddress address)
    {
        var device = GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        var room = GetRoom(device.RoomId);
        _devices.Remove(device);
        if (room is null)
        {
            House.RemoveDevice(device);
        }
        else
        {
            room.RemoveDevice(device);
            if (room.MasterRadioAddress == address)
            {
                var next = room.Devices.FirstOrDefault(candidate =>
                    candidate.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus);
                room.SetMaster(next?.RadioAddress ?? new RadioAddress(0));
            }
        }

        _events.Add(new DeviceStateChanged(address, "Removed"));
    }

    /// <summary>Entfernt einen Raum, der keine Geräte mehr enthält.</summary>
    public void RemoveRoom(int roomId)
    {
        var room = RequireRoom(roomId);
        if (room.Devices.Count > 0)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, $"Room {roomId} still contains devices.");
        }

        _rooms.Remove(room);
        _events.Add(new RoomStateChanged(roomId, "Removed"));
    }

    /// <summary>
    /// Verschiebt ein Gerät in einen anderen Raum (<paramref name="newRoomId"/> == 0 = House).
    /// Ein Master-Thermostat gibt seine Rolle an das nächste Thermostat ab; das Gerät wird
    /// im Zielraum Master, wenn der Raum noch keinen Master hat.
    /// </summary>
    public void MoveDevice(RadioAddress address, int newRoomId)
    {
        var device = GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        if (device.RoomId == newRoomId)
        {
            return;
        }

        var target = newRoomId == House.Id ? null : RequireRoom(newRoomId);
        var source = GetRoom(device.RoomId);

        // Zielraum zuerst prüfen (Limits je Gerätetyp); erst danach mutieren.
        if (target is not null)
        {
            target.EnsureCanAccept(device);
        }

        if (source is null)
        {
            House.RemoveDevice(device);
        }
        else
        {
            source.RemoveDevice(device);
            if (source.MasterRadioAddress == address)
            {
                var next = source.Devices.FirstOrDefault(candidate =>
                    candidate.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus);
                source.SetMaster(next?.RadioAddress ?? new RadioAddress(0));
            }
        }

        if (target is null)
        {
            House.AddDevice(device);
        }
        else
        {
            target.AddDevice(device);
            if (target.MasterRadioAddress.Value == 0
                && device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus)
            {
                target.SetMaster(address);
            }
        }

        _events.Add(new DeviceStateChanged(address, "Moved"));
    }

    /// <summary>
    /// Setzt das erste Heizkörperthermostat zum Master, wenn der Raum noch keinen hat
    /// (Master-Adresse 0). Wird nach dem Zuordnen eines neuen Geräts aufgerufen.
    /// </summary>
    public void EnsureRoomMaster(int roomId)
    {
        var room = RequireRoom(roomId);
        if (room.MasterRadioAddress.Value != 0)
        {
            return;
        }

        var candidate = room.Devices.FirstOrDefault(device =>
            device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus);
        if (candidate is not null)
        {
            room.SetMaster(candidate.RadioAddress);
        }
    }

    private Room RequireRoom(int roomId)
        => GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");

    public void SetDaylightSaving(bool value) => DaylightSaving = value;

    /// <summary>Events nach dem Verbrauch leeren (kein Zeitstempel, deterministisch).</summary>
    public IReadOnlyList<DomainEvent> TakeDomainEvents()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }
}
