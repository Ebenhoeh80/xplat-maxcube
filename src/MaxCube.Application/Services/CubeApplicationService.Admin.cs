using System.Globalization;
using System.Text;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Application.Services;

/// <summary>
/// Gremium Runde 5: vollständige Konfiguration, Anlernen, Strukturpflege, Zeit/Taster und
/// Funkauslastung. Jede schreibende Operation validiert zuerst und ändert das Modell erst,
/// nachdem der Cube sie angenommen hat. Strukturänderungen laufen über einen Entwurf: Der
/// Entwurf wird mutiert und validiert, Funktelegramme und Metadaten werden gesendet, und
/// erst danach wird dieselbe Mutation auf das echte Modell angewendet.
/// </summary>
public sealed partial class CubeApplicationService
{
    /// <summary>Länge eines einzelnen Anlernfensters; die Suche setzt mehrere zusammen.</summary>
    public static readonly TimeSpan InclusionWindow = TimeSpan.FromSeconds(10);

    /// <summary>Gesamtdauer des Anlernens (n:003c = 60 s, max.local.properties:9).</summary>
    public static readonly TimeSpan InclusionTotal = TimeSpan.FromSeconds(60);

    /// <summary>Sammelfenster für gebündelte Temperaturänderungen (indexController.js:119-121).</summary>
    public static readonly TimeSpan BundleWindow = TimeSpan.FromSeconds(3);

    /// <summary>INFERENZ: Schwelle für den Hinweis „hohe Funkauslastung“; der Original-Wert ist UNBEKANNT.</summary>
    public const int HighDutyCycleThreshold = 80;

    private static readonly int[] AllowedBoostMinutes = [0, 5, 10, 15, 20, 25, 30, 60];

    private readonly ICubeAdminGateway? _admin;
    private readonly ICubeNetworkConfigurator? _network;
    private readonly Dictionary<int, Temperature> _pendingTemperatures = new();
    private readonly object _bundleLock = new();
    private CancellationTokenSource? _bundleCts;

    /// <summary>Funkauslastung nach jedem gesendeten Telegramm.</summary>
    public event EventHandler<AirtimeStatus>? AirtimeChanged;

    public AirtimeStatus? LastAirtime { get; private set; }

    public bool SupportsAdministration => _admin is not null;

    public bool SupportsNetworkConfiguration => _network is not null;

    public bool SupportsMaintenance => _maintenance is not null;

    public int PendingTemperatureCount
    {
        get
        {
            lock (_bundleLock)
            {
                return _pendingTemperatures.Count;
            }
        }
    }

    private void OnAirtimeChanged(object? sender, AirtimeStatus status)
    {
        LastAirtime = status;
        if (status.DutyCycle >= HighDutyCycleThreshold)
        {
            RaiseWarning("WarnAirtime", $"Hohe Funkauslastung ({status.DutyCycle}); weitere Befehle später senden.", status.DutyCycle.ToString());
        }

        AirtimeChanged?.Invoke(this, status);
    }

    // ---------------------------------------------------------------------------------
    // Raumkonfiguration (0x11 / 0x12) und Wandthermostat-Anzeige (0x82)
    // ---------------------------------------------------------------------------------

    public async Task SetRoomConfigurationAsync(
        int roomId, RoomConfigurationValues values, CancellationToken cancellationToken = default)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var room = RequireRoom(roomId);
        var normalized = NormalizeConfiguration(values);
        var boostMinutes = room.BoostDurationMinutes;
        var boostValve = room.BoostValveAngle;
        var day = room.DecalcificationDay;
        var hour = room.DecalcificationHour;

        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            await admin.SetRoomConfigurationAsync(roomId, room.MasterRadioAddress, normalized, active)
                .ConfigureAwait(false);
            await ApplyAndPublishAsync(
                () => _current!.ApplyRoomConfiguration(
                    roomId, normalized.Comfort, normalized.Eco, normalized.Maximum, normalized.Minimum,
                    normalized.Offset, normalized.WindowOpen, normalized.WindowOpenMinutes,
                    boostMinutes, boostValve, day, hour),
                $"Raumkonfiguration {roomId} gesendet",
                active).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Boost und Entkalkung (0x12). Maximale Ventilöffnung und Ventil-Offset werden unverändert
    /// aus dem gelesenen Cube-Zustand zurückgeschrieben; ohne gelesene Rohwerte ist Senden gesperrt.
    /// </summary>
    public Task SetBoostAndDecalcificationAsync(
        int roomId, int boostMinutes, int boostValveAngle, DayOfWeek day, int hour,
        CancellationToken cancellationToken = default)
    {
        var room = RequireRoom(roomId);
        return SetValveFunctionsAsync(
            roomId, boostMinutes, boostValveAngle, day, hour, room.MaxValveRaw, room.ValveOffsetRaw,
            $"Boost/Entkalkung {roomId} gesendet", cancellationToken);
    }

    /// <summary>Ventil-Offset (SetRoomConfigValveOffset) in Prozent 0..100.</summary>
    public Task SetValveOffsetAsync(int roomId, int percent, CancellationToken cancellationToken = default)
    {
        if (percent is < 0 or > 100)
        {
            throw new DomainValidationException(DomainValidationCodes.Validation, "Ventil-Offset muss 0..100 % sein.");
        }

        var room = RequireRoom(roomId);
        return SetValveFunctionsAsync(
            roomId, room.BoostDurationMinutes, room.BoostValveAngle, room.DecalcificationDay,
            room.DecalcificationHour, room.MaxValveRaw, ValveOffsetFromPercent(percent),
            $"Ventil-Offset {roomId}={percent} % gesendet", cancellationToken);
    }

    public static int ValveOffsetFromPercent(int percent) => (int)Math.Round(percent * 255 / 100.0);

    public static int ValveOffsetToPercent(int raw) => (int)Math.Round(raw * 100 / 255.0);

    private async Task SetValveFunctionsAsync(
        int roomId, int boostMinutes, int boostValveAngle, DayOfWeek day, int hour,
        int maxValveRaw, int valveOffsetRaw, string audit, CancellationToken cancellationToken)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var room = RequireRoom(roomId);
        if (!room.HasValveConfiguration)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                "Die Ventilwerte dieses Raums wurden nicht vom Cube gelesen; Senden ist gesperrt.");
        }

        if (!AllowedBoostMinutes.Contains(boostMinutes))
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Boost-Dauer: 0, 5, 10, 15, 20, 25, 30 oder 60 Minuten.");
        }

        if (boostValveAngle is < 0 or > 100 || boostValveAngle % 5 != 0)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Boost-Ventilöffnung: Vielfache von 5 % bis 100 %.");
        }

        if (hour is < 0 or > 23)
        {
            throw new DomainValidationException(DomainValidationCodes.Validation, "Entkalkungsstunde: 0..23.");
        }

        var values = new ValveFunctionValues(boostMinutes, boostValveAngle, day, hour, maxValveRaw, valveOffsetRaw);
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            await admin.SetValveFunctionsAsync(roomId, room.MasterRadioAddress, values, active).ConfigureAwait(false);
            await ApplyAndPublishAsync(
                () =>
                {
                    _current!.ApplyRoomConfiguration(
                        roomId, room.ComfortTemperature, room.EcoTemperature, room.MaximumTemperature,
                        room.MinimumTemperature, room.TemperatureOffset, room.WindowOpenTemperature,
                        room.WindowOpenDurationMinutes, boostMinutes, boostValveAngle, day, hour);
                    _current.ApplyValveConfiguration(roomId, maxValveRaw, valveOffsetRaw);
                },
                audit,
                active).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    public async Task SetWallThermostatDisplayAsync(
        RadioAddress wallThermostat, bool showActualTemperature, CancellationToken cancellationToken = default)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var device = RequireCube().GetDevice(wallThermostat)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {wallThermostat} does not exist.");
        if (device.DeviceType != DeviceType.WallMountedThermostat)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Die Anzeigeumschaltung gilt nur für Wandthermostate.");
        }

        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await admin.SetDisplayModeAsync(wallThermostat, showActualTemperature, lease.Token).ConfigureAwait(false);
            await AuditAsync(
                $"Anzeige {device.Name}: {(showActualTemperature ? "Ist" : "Soll")}temperatur",
                lease.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static RoomConfigurationValues NormalizeConfiguration(RoomConfigurationValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var comfort = Quantize(values.Comfort);
        var eco = Quantize(values.Eco);
        var maximum = Quantize(values.Maximum);
        var minimum = Quantize(values.Minimum);
        var windowOpen = Quantize(values.WindowOpen);
        var offset = Math.Round(values.Offset * 2, MidpointRounding.AwayFromZero) / 2;

        if (minimum.Value > maximum.Value)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Die Mindesttemperatur darf nicht über der Höchsttemperatur liegen.");
        }

        foreach (var (name, value) in new[] { ("ECO", eco), ("Komfort", comfort), ("Fenster offen", windowOpen) })
        {
            if (value.Value < minimum.Value || value.Value > maximum.Value)
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation,
                    $"{name}-Temperatur muss zwischen Minimum und Maximum liegen.");
            }
        }

        if (offset is < -3.5 or > 3.5)
        {
            throw new DomainValidationException(DomainValidationCodes.Validation, "Temperatur-Offset: -3,5 bis +3,5 °C.");
        }

        // Schreib- und Lesetabelle der Fensterdauer stimmen nur bis 30 Minuten überein
        // (Schreiben: Minuten/5, Lesen: 3-Bit-Tabelle mit 60 als Code 7).
        if (values.WindowOpenMinutes is < 0 or > 30 || values.WindowOpenMinutes % 5 != 0)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Fenster-offen-Dauer: 0 bis 30 Minuten in 5er-Schritten.");
        }

        return new RoomConfigurationValues(comfort, eco, maximum, minimum, offset, windowOpen, values.WindowOpenMinutes);
    }

    // ---------------------------------------------------------------------------------
    // Gebündelte Temperaturänderungen
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Sammelt Temperaturänderungen und sendet pro Raum nur den letzten Wert gemeinsam, wenn das
    /// Fenster abläuft (indexController.js:119-121,187-217; maxCubeModel.js:178-200). Das spart
    /// Funkverkehr, weil jeder Zwischenwert sonst ein eigenes Funktelegramm kostet.
    /// </summary>
    public void QueueRoomTemperature(int roomId, Temperature temperature)
    {
        RequireOnline();
        RequireRoom(roomId);
        var quantized = Quantize(temperature);
        CancellationToken token;
        var start = false;
        lock (_bundleLock)
        {
            _pendingTemperatures[roomId] = quantized;
            if (_bundleCts is null)
            {
                _bundleCts = new CancellationTokenSource();
                start = true;
            }

            token = _bundleCts.Token;
        }

        if (start)
        {
            _ = Task.Run(() => FlushAfterWindowAsync(token));
        }
    }

    private async Task FlushAfterWindowAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(BundleWindow, _timeProvider, token).ConfigureAwait(false);
            await FlushPendingTemperaturesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Das Fenster wurde durch ein sofortiges Senden oder Trennen verworfen.
        }
    }

    /// <summary>Sendet alle gesammelten Temperaturen sofort; liefert die Anzahl gesendeter Räume.</summary>
    public async Task<int> FlushPendingTemperaturesAsync(CancellationToken cancellationToken = default)
    {
        KeyValuePair<int, Temperature>[] pending;
        lock (_bundleLock)
        {
            pending = _pendingTemperatures.ToArray();
            _pendingTemperatures.Clear();
            var cts = _bundleCts;
            _bundleCts = null;
            if (cts is not null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        var sent = 0;
        foreach (var (roomId, temperature) in pending)
        {
            try
            {
                await SetRoomTemperatureAsync(roomId, temperature, cancellationToken).ConfigureAwait(false);
                sent++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseWarning("WarnBatchFailed", $"Gebündelte Temperatur für Raum {roomId} nicht gesendet: {exception.Message}", roomId.ToString());
            }
        }

        return sent;
    }

    // ---------------------------------------------------------------------------------
    // Anlernen (Inclusion) und Zuordnung
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Sucht bis zu <paramref name="total"/> nach einem neuen Gerät, in Fenstern von
    /// <see cref="InclusionWindow"/>. Der Cube erhält nach jedem Fenster ein sauberes Ende,
    /// ein Abbruch wirkt dadurch spätestens am Fensterende.
    /// </summary>
    public async Task<FoundDevice?> SearchNewDeviceAsync(
        TimeSpan total, CancellationToken cancellationToken = default)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var cube = RequireCube();
        var deadline = _timeProvider.GetUtcNow() + total;
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            FoundDevice? found = null;
            while (found is null && _timeProvider.GetUtcNow() < deadline)
            {
                active.ThrowIfCancellationRequested();
                var remaining = deadline - _timeProvider.GetUtcNow();
                var window = remaining < InclusionWindow ? remaining : InclusionWindow;
                if (window < TimeSpan.FromSeconds(1))
                {
                    window = TimeSpan.FromSeconds(1);
                }

                found = await admin.SearchNewDeviceAsync(window, active).ConfigureAwait(false);
                if (found is not null && cube.GetDevice(found.Address) is not null)
                {
                    found = null; // bereits bekanntes Gerät, weitersuchen
                }
            }

            if (found is not null)
            {
                await AuditAsync(
                    $"Neues Gerät gefunden: {found.Type} {found.Address} ({found.SerialNumber})", active)
                    .ConfigureAwait(false);
            }

            return found;
        }
        finally
        {
            try
            {
                await admin.EndInclusionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseWarning("WarnInclusionStopFailed", "Anlernmodus konnte nicht sauber beendet werden: " + exception.Message);
            }

            lease.Dispose();
        }
    }

    /// <summary>
    /// Ordnet ein gefundenes Gerät einem bestehenden Raum, einem neuen Raum oder dem Haus
    /// (<paramref name="roomId"/> = 0) zu und schreibt Gruppe, Verknüpfungen und Metadaten.
    /// </summary>
    public async Task AssignNewDeviceAsync(
        FoundDevice found,
        string deviceName,
        int roomId,
        string? newRoomName,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmed(confirmed);
        ArgumentNullException.ThrowIfNull(found);
        var cube = RequireCube();
        var creatingRoom = !string.IsNullOrWhiteSpace(newRoomName);
        var isThermostat = found.Type is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus;
        if (creatingRoom && !isThermostat)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                "Ein neuer Raum braucht zuerst ein Heizkörperthermostat; Wandthermostate und Fensterkontakte gehören in bestehende Räume.");
        }

        var targetRoomId = creatingRoom ? cube.NextFreeRoomId() : roomId;
        var address = found.Address;

        void Mutate(Cube target)
        {
            if (creatingRoom)
            {
                var roomName = Device.ValidateName(newRoomName!.Trim());
                if (target.Rooms.Any(r => string.Equals(r.Name, roomName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new DomainValidationException(
                        DomainValidationCodes.Validation, $"A room named '{roomName}' already exists.");
                }

                target.AddRoom(new Room(targetRoomId, roomName, address));
            }

            var name = Device.ValidateName(deviceName?.Trim());
            if (name.Length == 0)
            {
                throw new DomainValidationException(DomainValidationCodes.NameIsNull, "Es wurde kein Name angegeben.");
            }

            if (target.Devices.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DomainValidationException(
                    DomainValidationCodes.DeviceNameAlreadyExists, $"A device named '{name}' already exists.");
            }

            target.AddDevice(new Device(
                address, found.Type, found.NumericType, new SerialNumber(found.SerialNumber), name, targetRoomId));
            if (targetRoomId != House.Id)
            {
                target.EnsureRoomMaster(targetRoomId);
            }
        }

        await RunStructureChangeAsync(
            $"Gerät {deviceName} ({found.Address}) in Raum {targetRoomId} aufgenommen",
            Mutate,
            async (admin, draft, token) =>
            {
                if (targetRoomId == House.Id)
                {
                    return;
                }

                await admin.SetGroupAddressAsync(address, targetRoomId, token).ConfigureAwait(false);
                var device = draft.GetDevice(address)!;
                await LinkAsync(admin, draft.GetRoom(targetRoomId)!, device, add: true, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------
    // Strukturpflege (Umbenennen, Löschen, Verschieben, Reihenfolge)
    // ---------------------------------------------------------------------------------

    public Task RenameRoomAsync(int roomId, string name, CancellationToken cancellationToken = default)
        => RunStructureChangeAsync(
            $"Raum {roomId} umbenannt in '{name}'",
            cube => cube.RenameRoom(roomId, name?.Trim() ?? string.Empty),
            null,
            cancellationToken);

    public Task RenameDeviceAsync(RadioAddress address, string name, CancellationToken cancellationToken = default)
        => RunStructureChangeAsync(
            $"Gerät {address} umbenannt in '{name}'",
            cube => cube.RenameDevice(address, name?.Trim() ?? string.Empty),
            null,
            cancellationToken);

    public Task ReorderRoomsAsync(IReadOnlyList<int> orderedRoomIds, CancellationToken cancellationToken = default)
        => RunStructureChangeAsync(
            "Raumreihenfolge geändert",
            cube => cube.ReorderRooms(orderedRoomIds),
            null,
            cancellationToken);

    /// <summary>
    /// Löscht ein Gerät. Das einzige Heizkörperthermostat eines Raums mit weiteren Geräten
    /// darf nicht entfernt werden (INFERENZ aus „Löschen nur bei Erlaubnis“,
    /// changeRoomStructureController.js:539-769): Wand- und Kontaktgeräte brauchen einen Master.
    /// </summary>
    public Task DeleteDeviceAsync(
        RadioAddress address, bool factoryReset, bool confirmed, CancellationToken cancellationToken = default)
    {
        RequireConfirmed(confirmed);
        var cube = RequireCube();
        var device = cube.GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        var room = cube.GetRoom(device.RoomId);
        if (room is not null
            && device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus
            && room.Devices.Count(d => d.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus) == 1
            && room.Devices.Count > 1)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                "Das einzige Heizkörperthermostat darf nicht gelöscht werden, solange andere Geräte im Raum sind.");
        }

        return RunStructureChangeAsync(
            $"Gerät {device.Name} ({address}) gelöscht{(factoryReset ? " (Werkseinstellungen)" : string.Empty)}",
            target => target.RemoveDevice(address),
            (admin, _, token) => admin.DeleteDevicesAsync([address], factoryReset, token),
            cancellationToken);
    }

    public Task DeleteRoomAsync(
        int roomId, bool factoryReset, bool confirmed, CancellationToken cancellationToken = default)
    {
        RequireConfirmed(confirmed);
        var room = RequireRoom(roomId);
        var addresses = room.Devices.Select(device => device.RadioAddress).ToArray();
        var name = room.Name;

        return RunStructureChangeAsync(
            $"Raum {roomId} '{name}' gelöscht ({addresses.Length} Gerät(e){(factoryReset ? ", Werkseinstellungen" : string.Empty)})",
            target =>
            {
                foreach (var address in addresses)
                {
                    target.RemoveDevice(address);
                }

                target.RemoveRoom(roomId);
            },
            addresses.Length == 0
                ? null
                : (admin, _, token) => admin.DeleteDevicesAsync(addresses, factoryReset, token),
            cancellationToken);
    }

    /// <summary>Verschiebt ein Gerät; liefert einen Hinweis (z. B. zum Modus), sonst null.</summary>
    public async Task<string?> MoveDeviceAsync(
        RadioAddress address, int newRoomId, bool confirmed, CancellationToken cancellationToken = default)
    {
        RequireConfirmed(confirmed);
        var cube = RequireCube();
        var device = cube.GetDevice(address)
            ?? throw new DomainValidationException(
                DomainValidationCodes.InvalidDeviceRadioAddress, $"Device {address} does not exist.");
        var oldRoomId = device.RoomId;
        if (oldRoomId == newRoomId)
        {
            return null;
        }

        var oldRoom = cube.GetRoom(oldRoomId);
        var oldPartners = oldRoom is null
            ? Array.Empty<Device>()
            : oldRoom.Devices.Where(partner => partner.RadioAddress != address).ToArray();
        var deviceName = device.Name;

        await RunStructureChangeAsync(
            $"Gerät {deviceName} ({address}) von Raum {oldRoomId} nach Raum {newRoomId} verschoben",
            target => target.MoveDevice(address, newRoomId),
            async (admin, draft, token) =>
            {
                var oldAnchor = oldRoom?.Devices.FirstOrDefault(candidate =>
                    candidate.RadioAddress != address
                    && candidate.DeviceType is DeviceType.HeatingThermostat
                        or DeviceType.HeatingThermostatPlus or DeviceType.WallMountedThermostat);
                if (oldAnchor is not null)
                {
                    await admin.SetLinkPartnerInRoomAsync(
                        oldAnchor.RadioAddress, oldRoomId, address, device.NumericDeviceType, false, token)
                        .ConfigureAwait(false);
                }

                foreach (var partner in oldPartners.Where(partner => IsLinkable(device, partner)))
                {
                    await admin.SetLinkPartnerAsync(address, partner.RadioAddress, partner.NumericDeviceType, false, token)
                        .ConfigureAwait(false);
                    await admin.SetLinkPartnerAsync(partner.RadioAddress, address, device.NumericDeviceType, false, token)
                        .ConfigureAwait(false);
                }

                if (newRoomId == House.Id)
                {
                    await admin.RemoveGroupAddressAsync(address, oldRoomId, token).ConfigureAwait(false);
                    return;
                }

                await admin.SetGroupAddressAsync(address, newRoomId, token).ConfigureAwait(false);
                await LinkAsync(admin, draft.GetRoom(newRoomId)!, draft.GetDevice(address)!, add: true, token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus
            ? "Der Modus des verschobenen Thermostats kann sich ändern; Raumprogramm und Modus bitte prüfen."
            : null;
    }

    private static bool IsLinkable(Device a, Device b)
    {
        static bool IsHeating(Device device)
            => device.DeviceType is DeviceType.HeatingThermostat or DeviceType.HeatingThermostatPlus;

        static bool IsSensor(Device device)
            => device.DeviceType is DeviceType.WallMountedThermostat or DeviceType.ShutterContact;

        return (IsHeating(a) && IsSensor(b)) || (IsSensor(a) && IsHeating(b));
    }

    private static async Task LinkAsync(
        ICubeAdminGateway admin, Room room, Device device, bool add, CancellationToken cancellationToken)
    {
        // Java ergänzt beim Anlernen/Entfernen genau ein Gruppenkopf-Telegramm von einem
        // vorhandenen Thermostat an das neue Gerät (AssociateNewDeviceToRoom.java:91-94,
        // DisassociateDeviceFromRoom.java:100-103). Die direkten Paar-Verlinkungen bleiben.
        var anchor = room.Devices.FirstOrDefault(candidate =>
            candidate.RadioAddress != device.RadioAddress
            && candidate.DeviceType is DeviceType.HeatingThermostat
                or DeviceType.HeatingThermostatPlus or DeviceType.WallMountedThermostat);
        if (anchor is not null)
        {
            await admin.SetLinkPartnerInRoomAsync(
                anchor.RadioAddress, room.Id, device.RadioAddress, device.NumericDeviceType, add, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var partner in room.Devices.Where(candidate =>
            candidate.RadioAddress != device.RadioAddress && IsLinkable(device, candidate)))
        {
            await admin.SetLinkPartnerAsync(
                device.RadioAddress, partner.RadioAddress, partner.NumericDeviceType, add, cancellationToken)
                .ConfigureAwait(false);
            await admin.SetLinkPartnerAsync(
                partner.RadioAddress, device.RadioAddress, device.NumericDeviceType, add, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RunStructureChangeAsync(
        string audit,
        Action<Cube> mutate,
        Func<ICubeAdminGateway, Cube, CancellationToken, Task>? radio,
        CancellationToken cancellationToken)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var current = RequireCube();
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            // 1. Entwurf: Validierung, bevor irgendetwas gesendet wird.
            var draft = CubeSnapshot.From(current, _clock.Now).ToCube();
            mutate(draft);

            // 2. Funktelegramme, 3. Metadaten (mit Rundreise-Sicherheitsprüfung im Gateway).
            if (radio is not null)
            {
                await radio(admin, draft, active).ConfigureAwait(false);
            }

            await admin.WriteMetadataAsync(draft, active).ConfigureAwait(false);

            // 4. Erst jetzt das echte Modell ändern und veröffentlichen.
            await ApplyAndPublishAsync(() => mutate(current), audit, active).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                await _store.AppendAuditAsync(
                        $"Strukturänderung fehlgeschlagen: {exception.Message}",
                        AuditSource.System, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Die ursprüngliche Ausnahme bleibt maßgeblich.
            }

            throw;
        }
        finally
        {
            lease.Dispose();
        }
    }

    // ---------------------------------------------------------------------------------
    // Cube-Einstellungen: Taster, Zeitzone, Zeit, NTP, Netzwerk
    // ---------------------------------------------------------------------------------

    public Task<PushButtonConfiguration> ReadPushButtonAsync(CancellationToken cancellationToken = default)
        => RunReadAsync((admin, cube, token) => admin.ReadPushButtonAsync(cube.RfAddress, token), cancellationToken);

    public async Task SetPushButtonAsync(
        PushButtonConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var key in new[] { configuration.Upper, configuration.Lower })
        {
            if (key.Mode is PushButtonKeyMode.AutoWithTemperature or PushButtonKeyMode.Manual
                && (key.Temperature is null || key.Temperature.Value.Value is < 5.0 or > 30.0))
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation, "Diese Tastenfunktion braucht eine Temperatur zwischen 5,0 und 30,0 °C.");
            }
        }

        await RunWriteAsync(
            (admin, cube, token) => admin.SetPushButtonAsync(cube.RfAddress, configuration, token),
            "Tasterkonfiguration gesendet",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TimeZoneConfiguration> ReadTimeZoneAsync(CancellationToken cancellationToken = default)
        => RunReadAsync((admin, cube, token) => admin.ReadTimeZoneAsync(cube.RfAddress, token), cancellationToken);

    public Task SetTimeZoneAsync(TimeZoneConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return RunWriteAsync(
            async (admin, cube, token) =>
            {
                await admin.SetTimeZoneAsync(cube.RfAddress, configuration, null, token).ConfigureAwait(false);
                cube.SetDaylightSaving(configuration.UsesDaylightSaving);
            },
            $"Zeitzone {configuration.Winter.Name}/{configuration.Summer.Name} gesendet",
            cancellationToken);
    }

    /// <summary>
    /// Setzt die Cube-Zeit auf die aktuelle PC-Zeit, indem die bestehende Zeitzonenkonfiguration
    /// mit der UTC-Zeit erneut gesendet wird (setCubeTimeController.js:57-168; INFERENZ: Unix-Sekunden).
    /// </summary>
    public Task SetCubeTimeAsync(CancellationToken cancellationToken = default)
        => RunWriteAsync(
            async (admin, cube, token) =>
            {
                var zone = await admin.ReadTimeZoneAsync(cube.RfAddress, token).ConfigureAwait(false);
                await admin.SetTimeZoneAsync(cube.RfAddress, zone, _clock.Now.ToUniversalTime(), token)
                    .ConfigureAwait(false);
            },
            "Cube-Zeit gesetzt",
            cancellationToken);

    public Task<string> QueryNtpServersAsync(CancellationToken cancellationToken = default)
        => RunReadAsync((admin, _, token) => admin.QueryNtpServersAsync(token), cancellationToken);

    public Task SetNtpServersAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var cleaned = servers.Select(server => server.Trim()).Where(server => server.Length > 0).ToArray();
        if (cleaned.Length is < 1 or > 2 || cleaned.Any(server => server.Length < 2 || server.Contains(',') || server.Contains(' ')))
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Ein oder zwei NTP-Server ohne Komma und Leerzeichen angeben.");
        }

        return RunWriteAsync(
            (admin, _, token) => admin.SetNtpServersAsync(cleaned, token),
            $"NTP-Server gesetzt: {string.Join(", ", cleaned)}",
            cancellationToken);
    }

    /// <summary>
    /// Wake-up (`z:`) ist ein schreibender Funkbefehl und wird deshalb nie automatisch
    /// in andere Schreibpfade eingebettet. Der Hersteller ruft ihn vor bestimmten
    /// Konfigurationsbefehlen auf (LocalController.java:472-501); unsere App verlangt
    /// eine ausdrückliche Bestätigung. Das <c>A:</c> bestätigt nur die Annahme durch den
    /// Cube, nicht die fachliche Wirkung an den Thermostaten.
    /// </summary>
    public Task WakeUpRoomAsync(
        int roomId, bool confirmed, int durationMinutes = 30, CancellationToken cancellationToken = default)
    {
        ValidateWakeupDuration(durationMinutes);
        RequireConfirmed(confirmed);
        RequireRoom(roomId);
        return RunWriteAsync(
            (admin, _, token) => admin.WakeUpRoomAsync(roomId, durationMinutes, token),
            $"Wake-up Raum {roomId} gesendet ({durationMinutes})",
            cancellationToken);
    }

    public Task WakeUpDeviceAsync(
        RadioAddress device, bool confirmed, int durationMinutes = 30, CancellationToken cancellationToken = default)
    {
        ValidateWakeupDuration(durationMinutes);
        RequireConfirmed(confirmed);
        var cube = RequireCube();
        if (!cube.Devices.Any(candidate => candidate.RadioAddress == device))
        {
            throw new DomainValidationException(DomainValidationCodes.Validation, "Gerät ist nicht verbunden.");
        }

        return RunWriteAsync(
            (admin, _, token) => admin.WakeUpDeviceAsync(device, durationMinutes, token),
            $"Wake-up Gerät {device} gesendet ({durationMinutes})",
            cancellationToken);
    }

    private static void ValidateWakeupDuration(int minutes)
    {
        if (minutes is < 0 or > 0xFF)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Wake-up-Dauer muss zwischen 0 und 255 liegen.");
        }
    }

    /// <summary>
    /// Neustart und Werksreset sind die einzigen Befehle, die den Cube selbst neu starten
    /// bzw. seine Konfiguration löschen. Beide verlangen eine explizite Bestätigung und
    /// laufen über denselben Gate/Audit-Pfad wie andere Schreibzugriffe (0.6.4).
    /// </summary>
    public Task RebootCubeAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        var maintenance = _maintenance ?? throw new InvalidOperationException("Wartung ist nicht verfügbar.");
        RequireConfirmed(confirmed);
        var endpoint = _lastEndpoint ?? throw new InvalidOperationException("Es ist kein Cube-Endpoint bekannt.");
        var cube = RequireCube();
        return RunWriteAsync(
            (_, _, token) => maintenance.RebootAsync(cube.SerialNumber.Value, endpoint.Host, token),
            "Cube-Neustart angefordert",
            cancellationToken);
    }

    public Task FactoryResetCubeAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        var maintenance = _maintenance ?? throw new InvalidOperationException("Wartung ist nicht verfügbar.");
        RequireConfirmed(confirmed);
        return RunWriteAsync(
            (_, _, token) => maintenance.FactoryResetAsync(token),
            "Cube-Werksreset angefordert",
            cancellationToken);
    }

    public async Task<CubeNetworkConfiguration> ReadNetworkConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var network = _network ?? throw new InvalidOperationException("Die Netzwerkkonfiguration ist nicht verfügbar.");
        var cube = RequireCube();
        var endpoint = _lastEndpoint ?? throw new InvalidOperationException("Es ist kein Cube-Endpoint bekannt.");
        var configuration = await network.ReadAsync(cube.SerialNumber.Value, endpoint.Host, cancellationToken)
            .ConfigureAwait(false);
        // Die c-Antwort enthält die gespeicherte Konfigurations-IP. Bei DHCP weicht sie von der
        // tatsächlich erreichbaren Adresse ab; beide Werte werden getrennt ausgewiesen (0.6.4).
        return configuration with { ActiveIp = endpoint.Host };
    }

    public async Task WriteNetworkConfigurationAsync(
        CubeNetworkConfiguration configuration, bool confirmed, CancellationToken cancellationToken = default)
    {
        RequireConfirmed(confirmed);
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateNetwork(configuration);
        RequireOnline();
        var network = _network ?? throw new InvalidOperationException("Die Netzwerkkonfiguration ist nicht verfügbar.");
        var cube = RequireCube();
        var endpoint = _lastEndpoint ?? throw new InvalidOperationException("Es ist kein Cube-Endpoint bekannt.");
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await network.WriteAsync(cube.SerialNumber.Value, endpoint.Host, configuration, lease.Token)
                .ConfigureAwait(false);
            await AuditAsync(
                $"Netzwerkkonfiguration gesendet ({(configuration.Dhcp ? "DHCP" : configuration.IpAddress)})",
                lease.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>Alle Adressfelder müssen vier Oktette sein, sonst wird nichts gesendet.</summary>
    public static void ValidateNetwork(CubeNetworkConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var (name, value) in new[]
        {
            ("IP-Adresse", configuration.IpAddress),
            ("Subnetzmaske", configuration.SubnetMask),
            ("Gateway", configuration.Gateway),
            ("DNS 1", configuration.Dns1),
            ("DNS 2", configuration.Dns2),
        })
        {
            if (!IsStrictIPv4(value))
            {
                throw new DomainValidationException(
                    DomainValidationCodes.Validation, $"{name} ist keine gültige IPv4-Adresse (vier Oktette 0..255).");
            }
        }
    }

    public static bool IsStrictIPv4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var octets = value.Split('.');
        return octets.Length == 4 && octets.All(octet =>
            octet.Length is >= 1 and <= 3 &&
            byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    // ---------------------------------------------------------------------------------
    // Einstellungen der App (Sprache, Log-Aufbewahrung, Bündelung)
    // ---------------------------------------------------------------------------------

    /// <summary>Löscht Audit-Einträge außerhalb der Aufbewahrung (auf 1..365 Tage begrenzt).</summary>
    public Task<int> CleanupAuditAsync(int retentionDays, CancellationToken cancellationToken = default)
        => _store.DeleteAuditOlderThanAsync(
            _clock.Now.AddDays(-Math.Clamp(retentionDays, 1, 365)), cancellationToken);

    public Task<string?> ReadSettingAsync(string key, CancellationToken cancellationToken = default)
        => _store.GetSettingAsync(key, cancellationToken);

    public Task WriteSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        => _store.SetSettingAsync(key, value, cancellationToken);

    // ---------------------------------------------------------------------------------
    // Diagnose und Neuladen
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Lokaler Diagnosebericht als Ersatz für „Fehlerbericht senden“ (BugReportWizard.java:19-128):
    /// nichts wird versendet; der Text enthält keine Zugangsdaten und lässt sich als Datei speichern.
    /// </summary>
    public async Task<string> BuildDiagnosticReportAsync(string appVersion, CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MAX! Cube – Diagnosebericht");
        builder.AppendLine($"Erstellt: {_clock.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"App-Version: {appVersion}");
        builder.AppendLine($"System: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        builder.AppendLine($".NET: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        if (_current is { } cube)
        {
            builder.AppendLine($"Cube: {cube.SerialNumber}, Firmware {cube.FirmwareVersion} ({(FirmwareSupport.IsSupported(cube.FirmwareVersion) ? "unterstützt" : "nicht unterstützt")})");
            builder.AppendLine($"Räume: {cube.Rooms.Count}, Geräte: {cube.Devices.Count}, Sommerzeit: {(cube.DaylightSaving ? "ja" : "nein")}");
        }
        else
        {
            builder.AppendLine("Cube: nicht verbunden");
        }

        builder.AppendLine($"Verbindung: {_gateway.State}, offline={IsOffline}, veraltet={IsStale}");
        builder.AppendLine($"Letzte Verbindung: {Describe(LastConnectedAt)}");
        builder.AppendLine($"Cube unerreichbar: {Describe(LastUnreachableAt)}");
        builder.AppendLine($"Reconnect erfolgreich: {Describe(LastReconnectedAt)}");
        if (LastAirtime is { } airtime)
        {
            builder.AppendLine($"Funkauslastung: {airtime.DutyCycle}, freie Speicherplätze: {airtime.FreeMemorySlots}");
        }

        builder.AppendLine();
        builder.AppendLine("Letzte Audit-Einträge:");
        foreach (var entry in (await _store.ReadAuditAsync(cancellationToken).ConfigureAwait(false)).TakeLast(50))
        {
            builder.AppendLine("  " + entry);
        }

        return builder.ToString();
    }

    private static string Describe(DateTimeOffset? value)
        => value is { } moment
            ? moment.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
            : "nicht belegt";

    /// <summary>
    /// Liest den Zustand nach einer Strukturänderung erneut vom Cube (Trennen, dann Verbinden
    /// zum letzten Endpoint). Der Cube bleibt Source of Truth.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = _lastEndpoint ?? throw new InvalidOperationException("Es ist kein Cube-Endpoint bekannt.");
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------
    // Hilfsmethoden
    // ---------------------------------------------------------------------------------

    private async Task<T> RunReadAsync<T>(
        Func<ICubeAdminGateway, Cube, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        var admin = RequireAdmin();
        var cube = RequireCube();
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await read(admin, cube, lease.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task RunWriteAsync(
        Func<ICubeAdminGateway, Cube, CancellationToken, Task> write, string audit, CancellationToken cancellationToken)
    {
        var admin = RequireAdmin();
        RequireOnline();
        var cube = RequireCube();
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await write(admin, cube, lease.Token).ConfigureAwait(false);
            await AuditAsync(audit, lease.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task AuditAsync(
        string message, CancellationToken cancellationToken, AuditSource source = AuditSource.User)
    {
        try
        {
            await _store.AppendAuditAsync(message, source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RaiseWarning("WarnAuditNotUpdated", "Gesendet, aber Audit nicht aktualisiert.");
        }
    }

    private ICubeAdminGateway RequireAdmin()
        => _admin ?? throw new InvalidOperationException("Diese Verbindung unterstützt keine erweiterten Funktionen.");

    private Cube RequireCube()
        => _current ?? throw new InvalidOperationException("Es ist kein Cube verbunden.");

    private static void RequireConfirmed(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Diese Änderung muss ausdrücklich bestätigt werden.");
        }
    }
}
