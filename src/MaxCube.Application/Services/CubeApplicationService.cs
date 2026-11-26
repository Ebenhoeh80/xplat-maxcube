using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Application.Services;

/// <summary>
/// Orchestriert Verbindung, Schreibbefehle und Cache. Alle fachlichen Mutationen laufen
/// über dieses eine Objekt, damit UI und Protokoll sich nicht gegenseitig direkt sehen.
/// </summary>
public sealed partial class CubeApplicationService
{
    private readonly ICubeGateway _gateway;
    private readonly ICubeStateStore _store;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ICubeMaintenanceGateway? _maintenance;
    private readonly OperationGate _gate = new();

    /// <summary>Obergrenze für den Gateway-Trennaufruf (N7).</summary>
    private static readonly TimeSpan GatewayDisconnectTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Exponentielles Reconnect: 1, 2, 4, … bis 30 s.</summary>
    private static readonly TimeSpan ReconnectBaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);

    private Cube? _current;
    private bool _offline;
    private bool _stale;
    private bool _reconnecting;
    private CubeEndpoint? _lastEndpoint;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;

    public CubeApplicationService(
        ICubeGateway gateway,
        ICubeStateStore store,
        IClock clock,
        TimeProvider? timeProvider = null,
        ICubeAdminGateway? admin = null,
        ICubeNetworkConfigurator? network = null,
        ICubeMaintenanceGateway? maintenance = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _admin = admin;
        _network = network;
        _maintenance = maintenance;
        _gateway.StateChanged += OnGatewayStateChanged;
        _gateway.CubeRefreshed += OnCubeRefreshed;
        _gateway.SessionUnreachable += OnSessionUnreachable;
        if (_admin is not null)
        {
            _admin.AirtimeChanged += OnAirtimeChanged;
        }
    }

    public event EventHandler<Cube>? StateChanged;

    public event EventHandler<CubeConnectionState>? ConnectionChanged;

    /// <summary>
    /// Ein Befehl wurde an den Cube gesendet, aber der lokale Cache/Audit konnte nicht
    /// geschrieben werden. Die UI zeigt diese Warnung, ohne den Erfolg am Cube infrage zu stellen.
    /// </summary>
    public event EventHandler<string>? Warning;

    /// <summary>
    /// Strukturierte Warnung für die UI: Code + Parameter statt eines bereits gerenderten
    /// Satzes. Das rohe <see cref="Warning"/> bleibt für Log und Support erhalten (0.6.4).
    /// </summary>
    public event EventHandler<ServiceWarning>? WarningCode;

    private void RaiseWarning(string code, string message, string? parameter = null)
    {
        Warning?.Invoke(this, message);
        WarningCode?.Invoke(this, new ServiceWarning(code, parameter));
    }

    /// <summary>Wird beim Wechsel zwischen aktuell und veraltet ausgelöst.</summary>
    public event EventHandler<bool>? StaleChanged;

    /// <summary>Letzter bekannter Zustand; null vor connect und nach Disconnect.</summary>
    public Cube? Current => _current;

    /// <summary>Endpoint, über den die aktuelle Verbindung tatsächlich läuft.</summary>
    public CubeEndpoint? ConnectedEndpoint => _lastEndpoint;

    /// <summary>
    /// Zeitpunkt, zu dem die aktuell angezeigten Daten vom Cube gelesen (oder aus dem Cache
    /// geladen) wurden. Die UI zeigt damit Herkunft und Alter statt stiller Defaultwerte.
    /// </summary>
    public DateTimeOffset? LastReadAt { get; private set; }

    /// <summary>Der letzte Cube-Zustand ist älter als der Live-Refresh zulässt.</summary>
    public bool IsStale => _stale;

    /// <summary>Gecachter Offline-Zustand ist nur lesbar.</summary>
    public bool IsOffline => _offline;

    public CubeConnectionState ConnectionState => _gateway.State;

    /// <summary>Bricht die laufende Operation ab (UI-Button „Abbrechen“).</summary>
    public void CancelActiveOperation() => _gate.CancelActive();

    public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
        => _store.ReadAuditAsync(cancellationToken);

    private void OnGatewayStateChanged(object? sender, CubeConnectionState state)
        => ConnectionChanged?.Invoke(this, state);

    /// <summary>l:-Antwort des Gateways: nur lesen und in den Cache schreiben.</summary>
    private async void OnCubeRefreshed(object? sender, Cube refreshed)
    {
        SetStale(false);
        LastReadAt = _clock.Now;
        try
        {
            await PersistAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RaiseWarning("WarnCacheNotUpdated", "Live-Zustand angezeigt, Cache nicht aktualisiert.");
        }

        StateChanged?.Invoke(this, refreshed);
    }

    private void OnSessionUnreachable(object? sender, EventArgs args)
    {
        SetStale(true);
        RaiseWarning("WarnCubeUnreachable", "Cube nicht erreichbar – versuche erneut zu verbinden.");
        StartReconnect();
    }

    private void SetStale(bool value)
    {
        if (_stale == value)
        {
            return;
        }

        _stale = value;
        StaleChanged?.Invoke(this, value);
    }

    private void StartReconnect()
    {
        if (_reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        _reconnectTask = Task.Run(() => ReconnectLoopAsync(token));
    }

    private void StopReconnect()
    {
        var cts = _reconnectCts;
        _reconnectCts = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        // Bewusst nicht sofort Dispose: der Reconnect-Task nutzt das Token noch.
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var delay = ReconnectBaseDelay;
        while (!cancellationToken.IsCancellationRequested && _lastEndpoint is not null)
        {
            try
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                _reconnecting = true;
                try
                {
                    await ConnectAsync(_lastEndpoint, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _reconnecting = false;
                }

                RaiseWarning("WarnReconnected", "Verbindung wiederhergestellt.");
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                delay = delay < ReconnectMaxDelay / 2 ? delay * 2 : ReconnectMaxDelay;
                RaiseWarning("WarnReconnectFailed", $"Reconnect fehlgeschlagen – nächster Versuch in {delay.TotalSeconds:0} s.", delay.TotalSeconds.ToString("0"));
            }
        }
    }

    /// <summary>
    /// Verbindet, übernimmt den frischen Cube-Zustand und schreibt ihn in den Cache.
    /// Der Cube ist Source of Truth; der Cache wird bewusst überschrieben (A3/A4).
    /// </summary>
    public async Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _lastEndpoint = endpoint;
        if (!_reconnecting)
        {
            StopReconnect();
        }

        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            var cube = await _gateway.ConnectAsync(endpoint, active).ConfigureAwait(false);
            _current = cube;
            LastReadAt = _clock.Now;
            try
            {
                await _store.SetSettingAsync("cube.serial", cube.SerialNumber.Value, active).ConfigureAwait(false);
                await _store.SetSettingAsync("cube.endpoint", $"{endpoint.Host}:{endpoint.Port}", active)
                    .ConfigureAwait(false);
                await PersistAsync(active).ConfigureAwait(false);
                await _store.AppendAuditAsync($"Connected to {endpoint.Host}:{endpoint.Port} ({cube.SerialNumber})", active)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Keine offene TCP-Session zurücklassen, wenn nur der Cache-Schreibpfad scheitert.
                _current = null;
                try
                {
                    await _gateway.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Die ursprüngliche Ausnahme bleibt maßgeblich.
                }

                throw;
            }

            _offline = false;
            SetStale(false);
            StateChanged?.Invoke(this, cube);
            return cube;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Letzter erfolgreich verbundener Endpoint aus dem Cache. Nur IPv4-Host:Port,
    /// damit eine beschädigte Setting-Zeile keinen Verbindungsversuch mit Unsinn startet.
    /// </summary>
    public async Task<CubeEndpoint?> GetLastEndpointAsync(CancellationToken cancellationToken = default)
    {
        var value = await _store.GetSettingAsync("cube.endpoint", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 ||
            !int.TryParse(value[(separator + 1)..], out var port) ||
            port is < 1 or > 65535)
        {
            return null;
        }

        return new CubeEndpoint(value[..separator], port);
    }

    /// <summary>
    /// Lädt den gecachten Zustand für die Offline-Anzeige. Liefert null, wenn noch nie
    /// ein Cube verbunden war. Der Cache wird als offline gekennzeichnet (KEIN Schreibbetrieb).
    /// </summary>
    public async Task<Cube?> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            // Nach einem Fault hat das Gateway Queue und Socket bereits geschlossen; der
            // Cache darf dann ebenfalls gelesen werden (Sonnet: LoadCached blieb sonst hängen).
            if (_gateway.State is CubeConnectionState.Connecting or CubeConnectionState.Connected)
            {
                throw new InvalidOperationException("The cache can only be loaded while disconnected.");
            }

            var serial = await _store.GetSettingAsync("cube.serial", active).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            var snapshot = await _store.LoadAsync(serial, active).ConfigureAwait(false);
            if (snapshot is null)
            {
                return null;
            }

            _current = snapshot.ToCube();
            LastReadAt = snapshot.CapturedAt;
            _offline = true;
            StateChanged?.Invoke(this, _current);
            return _current;
        }
        finally
        {
            lease.Dispose();
        }
    }

    public async Task SetRoomTemperatureAsync(
        int roomId, Temperature setPoint, CancellationToken cancellationToken = default)
    {
        RequireOnline();
        var room = RequireRoom(roomId);
        // Der Builder schneidet auf 0,5 °C ab. Ohne Quantisierung würde die UI einen
        // Wert anzeigen, den der Cube nie erhalten hat (Sonnet N5).
        setPoint = Quantize(setPoint);
        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            await _gateway.SetRoomTemperatureAsync(
                roomId, room.MasterRadioAddress, setPoint, active).ConfigureAwait(false);
            // Das Gateway sendet SetRoomPermanentModeCommand: Modus und Sollwert gehören
            // zusammen (ha-maxcube-erkenntnisse.md:216-218). Nur den Sollwert zu ändern
            // würde dem Cube widersprechen.
            await ApplyAndPublishAsync(() =>
            {
                _current!.SetControlMode(roomId, RoomControlMode.Permanently);
                _current.SetRoomTemperature(roomId, setPoint);
            }, $"Temperatur {roomId}={setPoint.Value:F1} (Permanently)", active).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    public async Task SetRoomControlModeAsync(
        int roomId,
        RoomControlMode mode,
        Temperature? setPoint,
        DateTimeOffset? until,
        CancellationToken cancellationToken = default)
    {
        RequireOnline();
        var room = RequireRoom(roomId);
        if (mode == RoomControlMode.Permanently && setPoint is null)
        {
            throw new DomainValidationException(DomainValidationCodes.Validation, "Permanent mode requires a setpoint.");
        }

        if (mode == RoomControlMode.Temporary && (setPoint is null || until is null))
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Temporary mode requires a setpoint and an end time.");
        }

        setPoint = QuantizeOrNull(setPoint);

        // SetRoomTemporaryMode.java:19-21 markiert die Endzeit mit @Future.
        if (mode == RoomControlMode.Temporary && until is not null && until <= _clock.Now)
        {
            throw new DomainValidationException(
                DomainValidationCodes.Validation, "Temporary end time must be in the future.");
        }

        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            await _gateway.SetRoomControlModeAsync(
                roomId, room.MasterRadioAddress, mode, setPoint, until, active).ConfigureAwait(false);
            await ApplyAndPublishAsync(
                () =>
                {
                    _current!.SetControlMode(roomId, mode);
                    // Auto wird ohne Sollwert gesendet; einen nie übertragenen Wert dürfen
                    // wir auch nicht im Cache oder in der Anzeige übernehmen.
                    if (mode != RoomControlMode.Auto && setPoint is not null)
                    {
                        _current.SetRoomTemperature(roomId, setPoint.Value);
                    }
                },
                $"Modus {roomId}={mode}",
                active).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// Tagesprofil aus dem vom Cube gelesenen Wochenprofil. Null, wenn kein Profil
    /// dekodiert wurde – die UI darf dann keinen Defaultplan senden (Gremium Runde 1, M-F).
    /// </summary>
    public DayProfile? GetWeekDayProfile(int roomId, DayOfWeek day)
    {
        var room = RequireRoom(roomId);
        return room.WeekProfile?.GetDay(day);
    }

    public bool HasWeekProfile(int roomId) => RequireRoom(roomId).HasWeekProfile;

    /// <summary>Gremium Runde 2: Sendesperre für den konkreten Wochentag.</summary>
    public bool HasWeekProfile(int roomId, DayOfWeek day)
        => RequireRoom(roomId).WeekProfile?.GetDay(day) is not null;

    /// <summary>Wendet ECO an, indem der belegte ECO-Sollwert gesendet wird.</summary>
    public Task ApplyEcoAsync(int roomId, CancellationToken cancellationToken = default)
        => SetRoomTemperatureAsync(roomId, RequireRoom(roomId).EcoTemperature, cancellationToken);

    /// <summary>Wendet COMFORT an, indem der belegte Komfort-Sollwert gesendet wird.</summary>
    public Task ApplyComfortAsync(int roomId, CancellationToken cancellationToken = default)
        => SetRoomTemperatureAsync(roomId, RequireRoom(roomId).ComfortTemperature, cancellationToken);

    public async Task SetWeekDayProfileAsync(
        int roomId, DayProfile profile, CancellationToken cancellationToken = default)
    {
        DayProfile.Validate(profile);
        RequireOnline();
        var room = RequireRoom(roomId);
        if (!room.HasWeekProfile || room.WeekProfile!.GetDay(profile.Day) is null)
        {
            // Gremium Runde 1 (M-F) + Runde 2: Ohne die vom Cube gelesenen 7x13 Werte
            // wäre das Senden ein blindes Überschreiben des echten Heizprogramms. Die
            // Sperre gilt pro Tag: ein vom Decoder verworfener Tag darf nicht durch
            // einen Default 21,0 °C/00:00 ersetzt werden.
            throw new DomainValidationException(
                DomainValidationCodes.Validation,
                $"Wochentag {profile.Day} wurde nicht vom Cube gelesen; Senden ist gesperrt.");
        }

        var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        try
        {
            await _gateway.SetWeekDayProfileAsync(
                roomId, room.MasterRadioAddress, profile, active).ConfigureAwait(false);
            try
            {
                await _store.AppendAuditAsync(
                    $"Wochenprofil {roomId}/{profile.Day} gesendet (S: Sendestatus, fachliche Annahme unbelegt)",
                    active).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseWarning("WarnAuditNotUpdated", "Profil gesendet, Audit nicht aktualisiert.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Bei zwei Telegrammen kann das erste bereits angekommen sein. Auch der
            // Fehlschlag gehört ins Audit, damit der Nutzer den Teilsendung kennt.
            try
            {
                await _store.AppendAuditAsync(
                    $"Wochenprofil {roomId}/{profile.Day} fehlgeschlagen: {exception.Message}",
                    CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>
    /// Trennt die Verbindung. Das App-Gate wartet höchstens 250 ms; das Gateway ist
    /// seinerseits zeitbegrenzt. Damit bleibt Trennen/Shutdown auch bei hängendem
    /// Transport unter 2 s (Gremium Runde 1, N7).
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopReconnect();
        SetStale(false);
        _gate.CancelActive();
        OperationLease lease;
        try
        {
            lease = await _gate.EnterAsync(cancellationToken, OperationGate.ShutdownAcquireTimeout)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _current = null;
            LastReadAt = null;
            _offline = false;
            return;
        }

        using (lease)
        {
            // Das Gateway bekommt bewusst kein already-cancelled Token: sonst bliebe
            // eine halb getrennte Session zurück. Zusätzlich ist der Aufruf begrenzt,
            // damit ein fehlerhaftes Gateway die App nicht festhält.
            try
            {
                await _gateway.DisconnectAsync(CancellationToken.None)
                    .WaitAsync(GatewayDisconnectTimeout).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException
                or OperationCanceledException or InvalidOperationException)
            {
                RaiseWarning("WarnDisconnectSlow", "Trennen hat länger als erwartet gedauert; Verbindung local zurückgesetzt.");
            }
            _current = null;
            LastReadAt = null;
            _offline = false;
            try
            {
                await _store.AppendAuditAsync("Disconnected", lease.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RaiseWarning("WarnDisconnectedAudit", "Verbindung getrennt, Audit nicht aktualisiert.");
            }
        }
    }

    /// <summary>Auf das 0,5-°-Raster des Drahtformats runden (RadiogramBuilder.cs:99).</summary>
    private static Temperature Quantize(Temperature value)
        => new(Math.Round(value.Value * 2, MidpointRounding.AwayFromZero) / 2);

    private static Temperature? QuantizeOrNull(Temperature? value)
        => value is null ? null : Quantize(value.Value);

    private void RequireOnline()
    {
        if (_offline)
        {
            throw new InvalidOperationException("The offline cache is read-only. Connect to a cube first.");
        }
    }

    private Room RequireRoom(int roomId)
    {
        var cube = _current
            ?? throw new InvalidOperationException("No cube is connected. Call ConnectAsync or LoadCachedAsync first.");
        return cube.GetRoom(roomId)
            ?? throw new DomainValidationException(DomainValidationCodes.Validation, $"Room {roomId} does not exist.");
    }

    /// <summary>
    /// Mutation und UI-Event gehören zusammen. Cache/Audit sind nachgelagert: Schlägt ein
    /// lokaler Schreibzugriff fehl, wurde der Befehl trotzdem gesendet und muss als solcher
    /// gemeldet werden (Sonnet M-A).
    /// </summary>
    private async Task ApplyAndPublishAsync(Action mutation, string audit, CancellationToken cancellationToken)
    {
        mutation();
        _current!.TakeDomainEvents(); // sonst wachsen die Domain-Events unbegrenzt
        try
        {
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            await _store.AppendAuditAsync(audit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RaiseWarning("WarnCacheNotUpdated", "Gesendet, aber lokaler Cache nicht aktualisiert.");
        }

        StateChanged?.Invoke(this, _current!);
    }

    private Task PersistAsync(CancellationToken cancellationToken)
        => _current is null
            ? Task.CompletedTask
            : _store.SaveAsync(CubeSnapshot.From(_current, _clock.Now), cancellationToken);
}
