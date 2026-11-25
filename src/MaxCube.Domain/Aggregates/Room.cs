using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Domain.Aggregates;

/// <summary>
/// Heizraum als Steuereinheit. Die Laufzeit-Defaults stammen aus Room.java:43-64 und
/// weichen bewusst von extracted/essentials/max.properties:2-13 ab (domain-model.md:138).
/// </summary>
public sealed class Room
{
    public Room(int id, string name, RadioAddress masterRadioAddress)
    {
        Id = id;
        Name = Device.ValidateName(name);
        MasterRadioAddress = masterRadioAddress;
    }

    public int Id { get; }
    public RadioAddress MasterRadioAddress { get; private set; }

    /// <summary>
    /// Name nur über das Aggregat änderbar (Raum umbenennen); ein neuer Name wird wie beim
    /// Anlegen validiert (ConfigureDeviceValidator.java:148-154).
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Maximale Ventilöffnung und Ventil-Offset als vom Cube gelesene Rohbytes (config[9],
    /// config[10]). <see cref="HasValveConfiguration"/> ist erst nach einem echten Lesen true;
    /// ohne diese Werte darf 0x12 nicht gesendet werden, damit kein Wert erfunden wird.
    /// </summary>
    public int MaxValveRaw { get; private set; } = 255;

    public int ValveOffsetRaw { get; private set; }

    public bool HasValveConfiguration { get; private set; }

    public Temperature EcoTemperature { get; private set; } = new(16.5);       // Room.java:45
    public Temperature ComfortTemperature { get; private set; } = new(21.5);  // Room.java:46
    public Temperature MaximumTemperature { get; private set; } = new(30.5);  // Room.java:47
    public Temperature MinimumTemperature { get; private set; } = new(4.5);   // max.properties:4 (OFF)
    public Temperature WindowOpenTemperature { get; private set; } = new(12.0);// Room.java:48
    public int WindowOpenDurationMinutes { get; private set; } = 15;           // Room.java:49
    public RoomControlMode ControlMode { get; private set; } = RoomControlMode.Auto;             // Room.java:50
    public RoomTemperatureMode TemperatureMode { get; private set; } = RoomTemperatureMode.NotAvailable; // Room.java:51
    public int BoostDurationMinutes { get; private set; } = 5;                 // Room.java:52
    public int BoostValveAngle { get; private set; } = 80;                     // Room.java:53
    public DayOfWeek DecalcificationDay { get; private set; } = DayOfWeek.Saturday; // Room.java:54
    public int DecalcificationHour { get; private set; } = 12;                 // Room.java:55

    // Belegt: Room.java:56-58. Das Maximum pro Typ stammt aus ConfigureDeviceValidator.java:80-99.
    private const int MaxHeatingThermostats = 8;
    private const int MaxWallMountedThermostats = 1;
    private const int MaxShutterContacts = 8;

    private readonly List<Device> _devices = [];
    public IReadOnlyList<Device> Devices => _devices;

    /// <summary>Solltemperatur; der Default 21,0 °C ist INFERENZ aus Temperature.Default (Room.java:61).</summary>
    public Temperature SetPointTemperature { get; private set; } = Temperature.Default;

    public MeasuredTemperature ActualTemperature { get; private set; } = new(0.0); // Room.java:39,134

    /// <summary>
    /// 0 Heizstunden ist keine echte Ist-Messung (protokoll-referenzen.md:139-140).
    /// Gemessene 4,5 °C oder 30,5 °C sind dagegen gültig und werden angezeigt.
    /// </summary>
    public bool HasActualTemperature => ActualTemperature.IsValid;

    /// <summary>Konfigurations-Offset; Default 0,0 (°C) aus max.properties:12.</summary>
    public double TemperatureOffset { get; private set; }

    /// <summary>
    /// Wochenprofil aus der C:-Konfiguration (config[11..]). Null bedeutet ausdrücklich
    /// „nicht vom Cube gelesen“ – der Editor darf dann kein Profil senden, statt einen
    /// Defaultplan ungesehen zu überschreiben (Gremium Runde 1, M-F).
    /// </summary>
    public WeekTemperatureProfile? WeekProfile { get; private set; }

    public bool HasWeekProfile => WeekProfile is not null;

    public Device? GetDevice(RadioAddress address) => _devices.FirstOrDefault(d => d.RadioAddress == address);

    /// <summary>Prüft Dubletten und Gerätelimits, ohne zu verändern (Basis für atomares Verschieben).</summary>
    internal void EnsureCanAccept(Device device)
    {
        if (GetDevice(device.RadioAddress) is not null)
        {
            throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress,
                $"Device {device.RadioAddress} already exists in room {Id}.");
        }

        var sameType = _devices.Count(d => d.DeviceType == device.DeviceType);
        var limit = device.DeviceType switch
        {
            DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus => MaxHeatingThermostats,
            DeviceType.WallMountedThermostat => MaxWallMountedThermostats,
            DeviceType.ShutterContact => MaxShutterContacts,
            _ => int.MaxValue,
        };

        if (sameType >= limit)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"Room {Id} already has {sameType} devices of type {device.DeviceType} (maximum {limit}).");
        }
    }

    internal void AddDevice(Device device)
    {
        if (GetDevice(device.RadioAddress) is not null)
        {
            throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress,
                $"Device {device.RadioAddress} already exists in room {Id}.");
        }

        var sameType = _devices.Count(d => d.DeviceType == device.DeviceType);
        var limit = device.DeviceType switch
        {
            DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus => MaxHeatingThermostats,
            DeviceType.WallMountedThermostat => MaxWallMountedThermostats,
            DeviceType.ShutterContact => MaxShutterContacts,
            _ => int.MaxValue,
        };

        if (sameType >= limit)
        {
            // INFERENZ: MAX1050 als allgemeiner Validierungscode; die Java-Exception
            // MaximumNoOfDevicesWithThisTypeInRoomException hat keinen eigenen Code in errorcodes.properties.
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"Room {Id} already has {sameType} devices of type {device.DeviceType} (maximum {limit}).");
        }

        _devices.Add(device);
        device.AssignToRoom(Id);
    }

    internal void Rename(string name) => Name = Device.ValidateName(name);

    internal void SetMaster(RadioAddress master) => MasterRadioAddress = master;

    internal void RemoveDevice(Device device) => _devices.Remove(device);

    internal void SetValveConfiguration(int maxValveRaw, int valveOffsetRaw)
    {
        MaxValveRaw = maxValveRaw;
        ValveOffsetRaw = valveOffsetRaw;
        HasValveConfiguration = true;
    }

    internal void SetWeekProfile(WeekTemperatureProfile profile) => WeekProfile = profile;

    internal void SetTemperature(Temperature setPoint) => SetPointTemperature = setPoint;

    internal void SetActualTemperature(MeasuredTemperature actual) => ActualTemperature = actual;

    internal void SetControlMode(RoomControlMode mode) => ControlMode = mode;

    internal void SetTemperatureMode(RoomTemperatureMode mode) => TemperatureMode = mode;

    internal void SetConfiguration(
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
        ComfortTemperature = comfort;
        EcoTemperature = eco;
        MaximumTemperature = maximum;
        MinimumTemperature = minimum;
        TemperatureOffset = offset;
        WindowOpenTemperature = windowOpen;
        WindowOpenDurationMinutes = windowOpenDuration;
        BoostDurationMinutes = boostDuration;
        BoostValveAngle = boostValveAngle;
        DecalcificationDay = decalcificationDay;
        DecalcificationHour = decalcificationHour;
    }
}
