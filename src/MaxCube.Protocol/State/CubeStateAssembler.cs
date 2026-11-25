using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Handshake;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.State;

/// <summary>
/// Baut das Domänen-Aggregat aus H:, M:, L: und C: auf. Reihenfolge und Feldpositionen
/// folgen MaxCubeStateFactory/DeviceStateSerializer/DeviceConfigurationSerializer;
/// der Cube bleibt Source of Truth (target-architecture.md:15).
/// </summary>
public static class CubeStateAssembler
{
    private const int ConfigurationHeaderLength = 18; // DeviceConfigurationSerializer.java:48-61

    // WindowOpenDurationDeviceConfigurationParameterDeserializer.java:7 (3 Bit → Minuten).
    private static readonly int[] WindowDurationValues = [0, 5, 10, 15, 20, 25, 30, 60];

    private sealed record ParsedDeviceState(
        RadioAddress Address,
        DeviceStateInfo StateInfo,
        RadioState RadioState,
        DeviceType DeviceType,
        int RoomId,
        Temperature? SetPoint,
        RoomControlMode? Mode,
        MeasuredTemperature? Actual);

    public static Cube Assemble(CubeHello hello, RawCubeState raw)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(raw);

        var metadata = raw.MetadataBlocks.Count == 0
            ? new CubeMetadataSnapshot(1, [], [], true)
            : MetadataDecoder.Decode(raw.GetMetadataBase64());

        var cube = new Cube(
            new SerialNumber(hello.SerialNumber),
            hello.FirmwareVersion,
            new RadioAddress(hello.RadioAddress),
            metadata.DaylightSaving);

        foreach (var room in metadata.Rooms)
        {
            cube.AddRoom(new Room(room.Id, room.Name, room.MasterRadioAddress));
        }

        foreach (var device in metadata.Devices)
        {
            try
            {
                cube.AddDevice(new Device(
                    device.RadioAddress,
                    device.DeviceType,
                    device.NumericDeviceType,
                    new SerialNumber(device.SerialNumber),
                    device.Name,
                    device.RoomId));
            }
            catch (DomainValidationException)
            {
                // Ein verwaistes oder das Limit sprengendes Gerät darf den gesamten
                // Connect nicht scheitern lassen (Source of Truth bleibt der Cube).
            }
        }

        ApplyConfigurations(cube, raw);
        ApplyDeviceStates(cube, raw.DeviceList);
        return cube;
    }

    private static void ApplyConfigurations(Cube cube, RawCubeState raw)
    {
        foreach (var (address, encoded) in raw.Configurations)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                continue; // Parse-Fehler faulten nicht (AP-1.3-Entscheidung).
            }

            if (bytes.Length < ConfigurationHeaderLength + 9)
            {
                continue;
            }

            var deviceType = DeviceTypeExtensions.FromValue(bytes[4]);
            if (deviceType is not (DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus or DeviceType.WallMountedThermostat))
            {
                continue;
            }

            var radioAddress = RadioAddress.FromBytes(bytes[1], bytes[2], bytes[3]);
            var device = cube.GetDevice(radioAddress);
            if (device is null)
            {
                continue;
            }

            var config = bytes.AsSpan(ConfigurationHeaderLength);
            // HeatingThermostatDeviceState.java:112-255; & 63 wie CubeConfigurationSerializer.java:79.
            var comfort = Temperature.FromWire(config[0] & 63);
            var eco = Temperature.FromWire(config[1] & 63);
            var maximum = Temperature.FromWire(config[2] & 63);
            var minimum = Temperature.FromWire(config[3] & 63);
            var offset = ((config[4] & 15) / 2.0) - 3.5;
            var windowOpen = Temperature.FromWire(config[5] & 63);
            // HeatingThermostatDeviceState.java:178,189,200: Fenster-Dauer Byte 6 Bit 0..2,
            // Boost-Dauer Byte 7 Bit 5..7 (beide über die VALUES-Tabelle), Boost-Ventil Byte 7 Bit 0..4.
            var windowOpenDuration = WindowDurationValues[config[6] & 7];
            var boostDuration = WindowDurationValues[(config[7] >> 5) & 7];
            var boostValve = (config[7] & 31) * 5;
            var decalcification = config[8];
            var dayCode = (decalcification >> 5) & 7;
            var day = DecalcificationDayFromCode(dayCode);
            var decalcificationHour = decalcification & 31;

            try
            {
                cube.ApplyRoomConfiguration(device.RoomId, comfort, eco, maximum, minimum, offset,
                    windowOpen, windowOpenDuration, boostDuration, boostValve, day, decalcificationHour);
                // config[9]/[10]: maximale Ventilöffnung und Ventil-Offset als Rohbytes.
                // Sie werden nur durchgereicht, damit 0x12 sie unverändert zurückschreibt.
                if (config.Length > 10)
                {
                    cube.ApplyValveConfiguration(device.RoomId, config[9], config[10]);
                }

                // M-F: Das Wochenprofil liegt im selben C:-Block ab config[11]. Fehlt es,
                // bleibt Room.HasWeekProfile false und der Editor darf nicht senden.
                var weekProfile = WeekProfileDecoder.Decode(config);
                if (weekProfile is not null)
                {
                    cube.ApplyWeekProfile(device.RoomId, weekProfile);
                }
            }
            catch (DomainValidationException)
            {
                // Ein verwaistes Gerät (roomId ohne Raum) darf den Gesamtstate nicht zerstören.
            }
        }
    }

    /// <summary>
    /// Übernimmt eine frische L:-Antwort in ein bestehendes Aggregat. Für den
    /// Live-Refresh ohne zweiten Handshake (Gremium Runde 1, Sitzungsüberwachung).
    /// </summary>
    public static void ApplyDeviceStates(Cube cube, string? deviceList)
    {
        ArgumentNullException.ThrowIfNull(cube);
        if (string.IsNullOrEmpty(deviceList))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(deviceList);
        }
        catch (FormatException)
        {
            return;
        }

        var parsed = new List<ParsedDeviceState>();
        var position = 0;
        while (position < bytes.Length)
        {
            var size = bytes[position++];
            if (position + size > bytes.Length)
            {
                break; // unvollständiger Block: Rest ignorieren, kein Fault (AP-1.3).
            }

            var block = bytes.AsSpan(position, size);
            position += size;
            if (size < 5)
            {
                // Zu klein für Adresse + FrameType + StateInfo (Bouni kennt 4-Byte-Blöcke).
                // Nur diesen Block überspringen, nicht alle folgenden Geräte verlieren.
                continue;
            }

            var address = RadioAddress.FromBytes(block[0], block[1], block[2]);
            var erroneousFrameType = block[3];
            var information = block[4];
            var state = block[5..];

            var isValid = (information & (1 << 4)) != 0;
            var isInitialized = (information & (1 << 1)) != 0;
            var isRfError = (information & (1 << 3)) != 0;
            var stateInfo = isValid && isInitialized ? DeviceStateInfo.Valid
                : !isInitialized ? DeviceStateInfo.NotInitialized : DeviceStateInfo.OutOfDate;
            var radioState = isRfError ? RadioStateForFrame(erroneousFrameType) : RadioState.Ok;

            var device = cube.GetDevice(address);
            if (device is null)
            {
                continue;
            }

            // Status/RadioState werden immer übernommen, Parameter nur bei gültigem Zustand
            // (DeviceStateSerializer.java:64-69). Ein fehlerhaftes Gerät darf den Connect
            // nicht abbrechen.
            try
            {
                cube.UpdateDeviceState(address, stateInfo, radioState);
            }
            catch (DomainValidationException)
            {
                continue;
            }

            if (!isValid || state.Length == 0)
            {
                continue;
            }

            try
            {
                // DeviceStateParameterWrapper dreht die 1-basierten bytePos-Werte um eins:
                // Modus bytePos=1 → state[0], Sollwert bytePos=3 → state[2],
                // Ist-Temperatur-Highbit bytePos=3 Bit 7 → state[2], LSB bytePos=7 → state[6]
                // (HeatingThermostatDeviceState.java:88-100, WallMountedThermostatDeviceState.java:302-317,
                //  DeviceStateParameterWrapper.java:18-22,41-44).
                Temperature? setPoint = null;
                RoomControlMode? mode = null;
                MeasuredTemperature? actual = null;

                if (device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus)
                {
                    setPoint = state.Length > 2 ? Temperature.FromWire(state[2] & 63) : null;
                    mode = state.Length > 0
                        ? (state[0] & 3) switch
                        {
                            0 => RoomControlMode.Auto,
                            1 => RoomControlMode.Permanently,
                            2 => RoomControlMode.Temporary,
                            _ => RoomControlMode.NotAvailable,
                        }
                        : null;
                }
                else if (device.DeviceType == DeviceType.WallMountedThermostat && state.Length > 6)
                {
                    var high = (state[2] & 0x80) != 0 && state[6] < 255 ? 256 : 0;
                    actual = MeasuredTemperature.FromWireTenths(state[6] + high);
                }

                parsed.Add(new ParsedDeviceState(
                    address, stateInfo, radioState, device.DeviceType, device.RoomId, setPoint, mode, actual));
            }
            catch (DomainValidationException)
            {
                // Dieses eine Gerät liefert unbrauchbare Parameter; der Rest bleibt nutzbar.
            }
        }

        // Java übernimmt Raumwerte vom Master-Thermostat, nicht vom zuletzt gesehenen Gerät
        // (DeviceStateSerializer.java:73-98). Wandthermostate liefern nur die Ist-Temperatur.
        foreach (var room in cube.Rooms)
        {
            var master = parsed
                .Where(state => state.RoomId == room.Id
                    && state.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus
                    && state.SetPoint is not null)
                .OrderBy(state => state.Address == room.MasterRadioAddress ? 0 : 1)
                .FirstOrDefault();
            if (master is not null)
            {
                try
                {
                    cube.ApplyDeviceTelemetry(
                        master.Address, master.StateInfo, master.RadioState,
                        setPoint: master.SetPoint, controlMode: master.Mode);
                }
                catch (DomainValidationException)
                {
                    // Ein einzelner Raum bleibt auf seinen Defaults, wenn sein State ungültig ist.
                }
            }

            var wall = parsed.FirstOrDefault(state => state.RoomId == room.Id && state.Actual is not null);
            if (wall is not null)
            {
                try
                {
                    cube.ApplyDeviceTelemetry(
                        wall.Address, wall.StateInfo, wall.RadioState, actual: wall.Actual);
                }
                catch (DomainValidationException)
                {
                    // Ist-Temperatur konnte nicht übernommen werden; Sollwert bleibt erhalten.
                }
            }
        }
    }

    /// <summary>
    /// Umkehrung des Java-Renderers WeekDay (<c>(value+1)%7</c>, RadiogramSpecification.java:281-285):
    /// 0 = Samstag, 1 = Sonntag, 2 = Montag … 6 = Freitag. Das deckt sich mit der Community-
    /// Spezifikation; die frühere Abbildung über <c>FromMaxValue</c> las Code 2 als Dienstag.
    /// </summary>
    public static DayOfWeek DecalcificationDayFromCode(int code) => code switch
    {
        0 => DayOfWeek.Saturday,
        1 => DayOfWeek.Sunday,
        >= 2 and <= 6 => DayOfWeekExtensions.FromMaxValue(code - 1),
        _ => DayOfWeek.Saturday,
    };

    /// <summary>FrameTypeCategory.toRadioState() (FrameType.java:61-63, FrameTypeCategory.java).</summary>
    private static RadioState RadioStateForFrame(byte code) => code switch
    {
        16 or 17 or 18 or 0x82 or 0x83 => RadioState.ConfigFailed,
        32 or 33 or 34 or 35 => RadioState.AssignFailed,
        64 or 65 or 0x80 => RadioState.ControlFailed,
        0x90 => RadioState.DeleteFailed,
        _ => RadioState.Error,
    };
}
