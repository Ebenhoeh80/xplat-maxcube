using MaxCube.Application.Abstractions;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using MaxCube.Protocol.Radiograms;
using MaxCube.Protocol.Session;
using MaxCube.Protocol.State;

namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// Adapter zwischen Application und Protokollschicht. Hält genau eine Verbindung mit
/// einem Transport, einem Writer und einer Queue (CubeCommandQueue.java:36,139-155).
/// </summary>
public sealed partial class CubeGateway : ICubeGateway, ICubeAdminGateway, IAsyncDisposable
{
    /// <summary>q: darf den Shutdown nicht aufhalten (Java wartet hier 2 s).</summary>
    private static readonly TimeSpan DisconnectCommandTimeout = TimeSpan.FromMilliseconds(700);

    /// <summary>Nach q: wartet der Hersteller auf EOF, bevor er die Session freigibt.</summary>
    private static readonly TimeSpan DisconnectEofTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Obergrenze für das Schließen von Queue und Socket.</summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Summe aus Gate, Monitor, q:, EOF und Socket-Close plus Reserve.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly CubeConnectionOptions _defaults;
    private readonly Func<CubeEndpoint, CubeConnectionOptions, ICubeTransport> _transportFactory;
    private readonly OperationGate _gate = new();
    private ICubeTransport? _transport;
    private CubeCommandQueue? _queue;
    private CubeSessionMonitor? _monitor;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private Cube? _sessionCube;
    private string? _sessionMetadata;
    private int _failedRefreshes;
    private int _unreachableRaised;
    private volatile CubeConnectionState _state = CubeConnectionState.Disconnected;
    private int _disposed;

    /// <param name="transportFactory">
    /// Ersetzbar für hardwarefreie Gateway-Tests; die App nutzt den TCP-Transport.
    /// </param>
    public CubeGateway(
        CubeConnectionOptions? defaults = null,
        Func<CubeEndpoint, CubeConnectionOptions, ICubeTransport>? transportFactory = null)
    {
        _defaults = defaults ?? new CubeConnectionOptions();
        _defaults.Validate();
        _transportFactory = transportFactory ?? ((_, options) => new TcpCubeTransport(options));
    }

    public CubeConnectionState State => _state;

    public event EventHandler<CubeConnectionState>? StateChanged;

    /// <summary>Frische Gerätewerte aus einer l:-Antwort (Live-Refresh).</summary>
    public event EventHandler<Cube>? CubeRefreshed;

    /// <summary>Sitzung gilt als nicht erreichbar; die Anwendung kann neu verbinden.</summary>
    public event EventHandler? SessionUnreachable;

    /// <summary>Funkauslastung nach jedem gesendeten Radiogramm (S:-Antwort).</summary>
    public event EventHandler<AirtimeStatus>? AirtimeChanged;

    /// <summary>Letzter Fehler eines Live-Refreshs; für Diagnose/Tests.</summary>
    public string? LastRefreshError { get; private set; }

    public async Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        // Ein Reconnect, der das Shutdown überlebt, darf keinen neuen Socket öffnen.
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(CubeGateway));
        }

        using var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var active = lease.Token;
        if (_queue is not null)
        {
            throw new InvalidOperationException("Already connected. Disconnect first.");
        }

        // Ein unreachable gewordener Monitor wurde nur abgebrochen, weil der Aufruf
        // aus dem Monitor-Task selbst kam. Erst nach seinem Ende darf ein neuer starten
        // (Gremium Runde 2, Technik/Protokoll).
        await StopMonitorAsync().ConfigureAwait(false);
        SetState(CubeConnectionState.Connecting);
        var options = _defaults with { Host = endpoint.Host, Port = endpoint.Port };
        var transport = _transportFactory(endpoint, options);
        try
        {
            await transport.ConnectAsync(active).ConfigureAwait(false);
            var hello = await new CubeHandshaker(options).PerformAsync(transport, active).ConfigureAwait(false);
            var raw = await new StateReceiver(options).ReceiveAsync(transport, hello, active).ConfigureAwait(false);
            var cube = CubeStateAssembler.Assemble(hello, raw);

            var queue = new CubeCommandQueue(transport, options);
            queue.Start();
            _transport = transport;
            _queue = queue;
            _sessionCube = cube;
            _sessionMetadata = raw.MetadataBlocks.Count == 0 ? null : raw.GetMetadataBase64();
            _failedRefreshes = 0;
            Volatile.Write(ref _unreachableRaised, 0);
            _monitor = new CubeSessionMonitor(options);
            _monitorCts = new CancellationTokenSource();
            _monitorTask = _monitor.RunAsync(OnWatchdogAsync, OnKeepAliveAsync, _monitorCts.Token);
            SetState(CubeConnectionState.Connected);
            return cube;
        }
        catch
        {
            await DisposeBoundedAsync(transport.DisposeAsync).ConfigureAwait(false);
            SetState(CubeConnectionState.Faulted);
            throw;
        }
    }

    public async Task SetRoomTemperatureAsync(
        int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default)
        => await SendAsync(new SetRoomPermanentModeCommand(master, roomId, setPoint), cancellationToken).ConfigureAwait(false);

    public async Task SetRoomControlModeAsync(
        int roomId,
        RadioAddress master,
        RoomControlMode mode,
        Temperature? setPoint,
        DateTimeOffset? until,
        CancellationToken cancellationToken = default)
    {
        CubeCommand<RadioStatus> command = mode switch
        {
            RoomControlMode.Auto => new SetRoomAutoModeCommand(master, roomId),
            RoomControlMode.Permanently => new SetRoomPermanentModeCommand(
                master, roomId, setPoint ?? throw new DomainValidationException(
                    DomainValidationCodes.Validation, "Permanent mode requires a setpoint.")),
            RoomControlMode.Temporary => new SetRoomTemporaryModeCommand(
                master, roomId,
                setPoint ?? throw new DomainValidationException(
                    DomainValidationCodes.Validation, "Temporary mode requires a setpoint."),
                until ?? throw new DomainValidationException(
                    DomainValidationCodes.Validation, "Temporary mode requires an end time.")),
            _ => throw new DomainValidationException(
                DomainValidationCodes.Validation, $"Mode {mode} cannot be sent to the cube."),
        };

        await SendAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetWeekDayProfileAsync(
        int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
    {
        var telegrams = WeekProfileSerializer.Serialize(profile, master, roomId);
        for (var index = 0; index < telegrams.Count; index++)
        {
            try
            {
                await SendAsync(new SetRoomWeekTemperatureProfileCommand(payload: telegrams[index]), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Der Cube bestätigt nicht mit A:. Bei zwei Telegrammen kann das erste
                // bereits angekommen sein; das muss im Fehlertext sichtbar werden.
                throw new CubeCommunicationFailedException(
                    index == 0
                        ? $"Week profile telegram 1/{telegrams.Count} failed."
                        : $"Week profile telegram {index + 1}/{telegrams.Count} failed; earlier telegrams may have been applied.",
                    exception);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // Erst die laufende I/O abbrechen, dann das Gate betreten. Ein Transport, der
        // Lesezugriffe ignoriert, darf weder Trennen noch Shutdown festhalten (N7).
        _gate.CancelActive();
        var candidate = _transport;
        OperationLease lease;
        try
        {
            lease = await _gate.EnterAsync(cancellationToken, OperationGate.ShutdownAcquireTimeout)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Gate weiterhin belegt: Socket zwangsweise schließen, damit der hängende
            // Lesevorgang abbricht, und den Zustand trotzdem vollständig aufräumen.
            // Ohne das Zurücksetzen scheitert der nächste Connect an "Already connected"
            // (Gremium Runde 2, alle drei Stimmen).
            await StopMonitorAsync().ConfigureAwait(false);
            var queue = _queue;
            _queue = null;
            _transport = null;
            _sessionCube = null;
            if (queue is not null)
            {
                await DisposeBoundedAsync(queue.DisposeAsync).ConfigureAwait(false);
            }

            // Auch bei hängender Queue den Socket schließen: der Cube erlaubt nur
            // eine TCP-Session und hält sie sonst bis zu seinem eigenen Timeout.
            if (candidate is not null)
            {
                await DisposeBoundedAsync(candidate.DisposeAsync).ConfigureAwait(false);
            }

            SetState(CubeConnectionState.Faulted);
            return;
        }

        using (lease)
        {
            await StopMonitorAsync().ConfigureAwait(false);
            _sessionCube = null;
            var queue = _queue;
            _queue = null;
            var transport = _transport;
            _transport = null;
            if (queue is not null)
            {
                // Der Cube erlaubt nur eine TCP-Session. Java sendet q: und schließt danach
                // (DisconnectCubeCommand.java:13-19). Das Zeitbudget liegt bewusst deutlich
                // unter 2 s, damit DisposeAsync/Shutdown nicht blockieren.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
                timeout.CancelAfter(DisconnectCommandTimeout);
                await ExecuteBoundedAsync(
                    queue.ExecuteAsync(new DisconnectCommand(DisconnectCommandTimeout), timeout.Token))
                    .ConfigureAwait(false);
                if (transport is not null)
                {
                    await AwaitDisconnectEofAsync(transport, lease.Token).ConfigureAwait(false);
                }

                await DisposeBoundedAsync(queue.DisposeAsync).ConfigureAwait(false);
            }

            // Die Queue disposed ihren Transport; zusätzlich direkt schließen, falls
            // ihr Worker hängt. TcpCubeTransport.DisposeAsync ist idempotent.
            if (transport is not null)
            {
                await DisposeBoundedAsync(transport.DisposeAsync).ConfigureAwait(false);
            }

            SetState(CubeConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// a: setzt das Gateway zurück. Der Herstellerclient erwartet kein ACK
    /// (ResetGatewayCubeCommand.java:11-12); die Queue schreibt nur die Zeile.
    /// </summary>
    public Task ResetGatewayAsync(CancellationToken cancellationToken = default)
        => SendAsync(new ResetGatewayCommand(), cancellationToken);

    private async Task<TResult> SendAsync<TResult>(CubeCommand<TResult> command, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var queue = _queue
            ?? throw new InvalidOperationException("No cube is connected.");
        try
        {
            var result = await queue.ExecuteAsync(command, lease.Token).ConfigureAwait(false);
            // Ein Nutzerbefehl ist keine Stille: der Watchdog darf danach nicht sofort l: senden.
            _monitor?.RecordActivity();
            if (result is RadioStatus radio)
            {
                AirtimeChanged?.Invoke(this, new AirtimeStatus(radio.DutyCycle, radio.FreeMemorySlots));
            }

            return result;
        }
        catch (Exception)
        {
            if (queue.IsFaulted)
            {
                // Timeouts und EOF faulten die Queue, sind aber keine CubeTransportException.
                // Ohne diesen Branch bliebe die Session nach dem ersten Fehler "Connected",
                // obwohl der Worker bereits alle Spätschreiber abgelehnt hat (Sonnet M-B).
                if (ReferenceEquals(_queue, queue))
                {
                    _queue = null;
                    _transport = null;
                }

                await StopMonitorAsync().ConfigureAwait(false);
                _sessionCube = null;
                await DisposeBoundedAsync(queue.DisposeAsync).ConfigureAwait(false);
                SetState(CubeConnectionState.Faulted);
            }

            throw;
        }
    }

    private async Task OnWatchdogAsync(CancellationToken cancellationToken)
    {
        if (_monitor is null)
        {
            return;
        }

        if (_monitor.Evaluate(_defaults.TimeProvider.GetUtcNow()) is CubeSessionState.Unreachable)
        {
            await MarkUnreachableAsync().ConfigureAwait(false);
            return;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task OnKeepAliveAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    /// <summary>
    /// l: liefert nur Gerätezustände; Metadaten/Konfiguration bleiben vom Connect gültig.
    /// Ein fehlgeschlagener Refresh faultet nicht sofort, aber drei in Folge gelten als
    /// nicht erreichbar (Gremium Runde 1, Sitzungsüberwachung).
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var queue = _queue;
        var cube = _sessionCube;
        if (queue is null || cube is null)
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_defaults.CommandTimeout);
            // ExecuteAsync liefert bereits den Body hinter "L:" (ResponseCommand.ParseBody).
            var body = await queue.ExecuteAsync(new ListDevicesCommand(), timeout.Token).ConfigureAwait(false);
            CubeStateAssembler.ApplyDeviceStates(cube, body);
            LastRefreshError = null;
            _failedRefreshes = 0;
            _monitor?.RecordActivity();
            CubeRefreshed?.Invoke(this, cube);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: kein Fehler, kein Reconnect.
        }
        catch (Exception exception)
        {
            LastRefreshError = exception.Message;
            if (Interlocked.Increment(ref _failedRefreshes) >= 3)
            {
                await MarkUnreachableAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Gemeinsamer Aufräumpfad für eine nicht mehr erreichbare Sitzung. Läuft bewusst
    /// ohne <see cref="StopMonitorAsync"/>, weil der Aufruf aus dem Monitor-Task selbst
    /// kommt; <see cref="ConnectAsync"/> wartet auf den alten Task, bevor ein neuer
    /// startet (Gremium Runde 2).
    /// </summary>
    private async Task MarkUnreachableAsync()
    {
        SetState(CubeConnectionState.Faulted);
        if (Interlocked.Exchange(ref _unreachableRaised, 1) == 0)
        {
            SessionUnreachable?.Invoke(this, EventArgs.Empty);
        }

        CancelMonitor();
        var queue = _queue;
        _queue = null;
        var transport = _transport;
        _transport = null;
        _sessionCube = null;
        if (queue is not null)
        {
            await DisposeBoundedAsync(queue.DisposeAsync).ConfigureAwait(false);
        }
        else if (transport is not null)
        {
            await DisposeBoundedAsync(transport.DisposeAsync).ConfigureAwait(false);
        }
    }

    private void CancelMonitor()
    {
        try
        {
            _monitorCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Der Monitor wurde bereits beim Trennen beendet.
        }
    }

    private async Task StopMonitorAsync()
    {
        var cts = _monitorCts;
        _monitorCts = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        var task = _monitorTask;
        _monitorTask = null;
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(CleanupTimeout).ConfigureAwait(false);
            }
            catch
            {
                // Der Watchdog wird beim Trennen bewusst abgebrochen.
            }
        }

        _monitor = null;
        cts.Dispose();
    }

    private static async Task ExecuteBoundedAsync(Task task)
    {
        try
        {
            await task.WaitAsync(DisconnectCommandTimeout).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning("Disconnect command failed: " + exception.Message);
        }
    }

    /// <summary>
    /// Der Hersteller liest nach q: bis EOF, bevor er die einzige TCP-Session
    /// freigibt (DisconnectCubeCommand.java:15-19). Begrenzt warten und das Ergebnis
    /// ignorieren: ein Cube ohne EOF darf das Beenden nicht blockieren.
    /// </summary>
    private static async Task AwaitDisconnectEofAsync(ICubeTransport transport, CancellationToken token)
    {
        try
        {
            await transport.ReadLineAsync(DisconnectEofTimeout, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning("Disconnect EOF not confirmed: " + exception.Message);
        }
    }

    private static async Task DisposeBoundedAsync(Func<ValueTask> dispose)
    {
        try
        {
            await dispose().AsTask().WaitAsync(CleanupTimeout).ConfigureAwait(false);
        }
        catch
        {
            // Der Socket ist bereits weg; der Zustand wird trotzdem zurückgesetzt.
        }
    }

    private void SetState(CubeConnectionState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Beim Shutdown q: senden und den Socket schließen. Der DI-Container ruft diese
    /// Methode für das Singleton auf; das explizite Disconnect in der UI ist damit
    /// kein Fehlerpfad mehr (Sonnet M-G).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // Zusätzliche Obergrenze, damit ein fehlerhafter Socket das Beenden
            // der Anwendung unter keinen Umständen blockiert (Gremium Runde 1, N7).
            await DisconnectAsync(CancellationToken.None)
                .WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch
        {
            // Ein fehlerhafter Socket darf das Beenden der Anwendung nicht blockieren.
        }
    }
}
