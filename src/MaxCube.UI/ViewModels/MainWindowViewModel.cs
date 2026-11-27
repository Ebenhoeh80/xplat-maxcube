using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.UI.Fakes;
using MaxCube.UI.Localization;
using MaxCube.UI.Services;
using MaxCube.UI;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.ViewModels;

/// <summary>Gemeinsame Modi für die Haussteuerung mehrerer ausgewählter Räume.</summary>
public enum HouseMode
{
    Auto,
    Eco,
    Comfort,
    Temporary,
}

/// <summary>
/// Haupt-ViewModel der Avalonia-App. Enthält bewusst keine Controls; alle Befehle
/// laufen über <see cref="CubeApplicationService"/> (target-architecture.md:215).
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly CubeApplicationService _service;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly ICubeDiscovery _discovery;
    private readonly Localizer _loc;
    private readonly IAppLog _log;
    // Avalonia aktualisiert {Binding Labels[Schlüssel]} nicht, wenn der Indexer nur Item[]
    // meldet. Die XAML bindet deshalb an dieses Objekt; es wird beim Sprachwechsel ersetzt.
    private Localizer _labels;

    /// <summary>Zeitbudget des Cube-Finders; entspricht dem Original (cubeScanTime=1800 ms).</summary>
    private static readonly TimeSpan DiscoveryDuration = TimeSpan.FromMilliseconds(1800);

    /// <summary>Port des physischen Cubes; Discovery liefert nur die IP.</summary>
    private const int CubeTcpPort = 62910;

    /// <summary>Ein StateChanged mit demselben Raum darf die getippte Eingabe nicht überschreiben.</summary>
    private bool _suppressRequestedTemperatureSync;

    /// <summary>
    /// Beim Neuaufbau der Raumliste (StateChanged/Sprachwechsel) werden alle vier
    /// tablokalen Auswahlen gesetzt. Die einzelnen Handler dürfen dabei nicht vier Mal
    /// dieselben Daten nachladen.
    /// </summary>
    private bool _suppressRoomSelectionHandlers;

    /// <summary>
    /// Raum, für den das Wochenprofil zuletzt aus dem Cube geladen wurde. Ein StateChanged
    /// mit demselben Raum darf die gerade bearbeiteten Schaltpunkte nicht zurücksetzen.
    /// </summary>
    private int? _weekProfileLoadedFor;

    /// <summary>Aktiver CTS für den sichtbaren Abbrechen-Button.</summary>
    private CancellationTokenSource? _actionCts;
    private LocalizedValue? _statusBeforeCancel;

    /// <summary>
    ///cwd 0.6.2: Ein riskanter Schreibbefehl wird erst nach einer sichtbaren Vorschau
    /// ausgeführt. Die Aktion wird dafür zwischengespeichert, nicht sofort gesendet.
    /// </summary>
    private PendingWrite? _pendingWrite;

    /// <summary>Letzte als gespeichert betrachtete Bearbeitung für den Dirty-Schutz, je Raum.</summary>
    private string? _savedWeekSignature;
    private int? _savedWeekRoomId;
    private string? _savedConfigSignature;
    private int? _savedConfigRoomId;

    private static readonly HashSet<string> ConfigSignatureProperties = new(StringComparer.Ordinal)
    {
        nameof(ConfigComfort), nameof(ConfigEco), nameof(ConfigMaximum), nameof(ConfigMinimum),
        nameof(ConfigWindowTemperature), nameof(ConfigWindowMinutes), nameof(ConfigOffset),
        nameof(BoostMinutes), nameof(BoostValveAngle), nameof(DecalcificationDay),
        nameof(DecalcificationHour), nameof(ValveOffsetPercent),
    };

    private sealed record PendingWrite(
        Func<CancellationToken, Task> Action, LocalizedValue ErrorPrefix, LocalizedValue? SuccessText,
        DataStatusViewModel? Status);

    /// <summary>Herence und Alter pro Bereich; nie in der Domäne, immer im ViewModel.</summary>
    private readonly DataStatusViewModel[] _statuses;

    private sealed record LocalizedBinding(LocalizedValue Value, Action<string> Setter);

    /// <summary>
    /// Merkt sich, wie ein gespeicherter Anzeigetext aus der aktuellen Sprache erzeugt wird.
    /// So wechseln auch Status-, Ergebnis- und Vorschautexte ohne Neustart oder Cube-Zugriff.
    /// </summary>
    private readonly Dictionary<string, LocalizedBinding> _localizedBindings = new(StringComparer.Ordinal);

    private void SetLocalized(string property, LocalizedValue? value, Action<string> setter)
    {
        if (value is null || string.IsNullOrEmpty(value.Current))
        {
            _localizedBindings.Remove(property);
            setter(string.Empty);
            return;
        }

        _localizedBindings[property] = new LocalizedBinding(value, setter);
        setter(value.Current);
    }

    private void RefreshLocalizedBindings()
    {
        foreach (var property in _localizedBindings.Keys.ToArray())
        {
            var binding = _localizedBindings[property];
            var value = binding.Value.In(_loc);
            _localizedBindings[property] = new LocalizedBinding(value, binding.Setter);
            binding.Setter(value.Current);
        }

        // Eine noch nicht bestätigte Vorschau trägt die Texte der alten Sprache. Sie werden
        // mitgewechselt; der Cube wird dadurch nicht kontaktiert.
        if (_pendingWrite is not null)
        {
            _pendingWrite = _pendingWrite with
            {
                ErrorPrefix = _pendingWrite.ErrorPrefix.In(_loc),
                SuccessText = _pendingWrite.SuccessText?.In(_loc),
            };
        }
    }

    private void SetStatus(LocalizedValue? value) => SetLocalized(nameof(StatusText), value, text => StatusText = text);

    private void SetSuccess(LocalizedValue? value) => SetLocalized(nameof(SuccessText), value, text => SuccessText = text);

    private void SetWarning(LocalizedValue? value) => SetLocalized(nameof(WarningText), value, text => WarningText = text);

    private void SetError(LocalizedValue? value) => SetLocalized(nameof(ErrorMessage), value, text => ErrorMessage = text);

    /// <summary>
    /// Wochenprofil-Fehler als lokalisierten Satz mit Renderfunktion statt als rohen
    /// Ausnahmetext; der Rohtext bleibt nur im Protokoll (0.6.5).
    /// </summary>
    private void SetLocalizedProfileError(Exception exception, string prefixKey)
    {
        var prefix = L(prefixKey);
        var code = ErrorCatalog.FindFirstCode(exception.Message);
        SetError(code is null
            ? L(localizer => $"{prefix.In(localizer).Current}: {localizer.Get("ErrorGeneric")}")
            : L("ErrorWithCode", prefix, code));
    }

    private void SetBusy(LocalizedValue? value) => SetLocalized(nameof(BusyText), value, text => BusyText = text);

    private void SetPreview(LocalizedValue? value) => SetLocalized(nameof(WritePreviewText), value, text => WritePreviewText = text);

    private void SetDiscovery(LocalizedValue? value) => SetLocalized(nameof(DiscoveryText), value, text => DiscoveryText = text);

    private void SetProfileRoom(LocalizedValue? value) => SetLocalized(nameof(ProfileRoomName), value, text => ProfileRoomName = text);

    private void SetAirtime(LocalizedValue? value) => SetLocalized(nameof(AirtimeText), value, text => AirtimeText = text);

    private void SetTimeZone(LocalizedValue? value) => SetLocalized(nameof(TimeZoneText), value, text => TimeZoneText = text);

    private void SetDiagnostic(LocalizedValue? value) => SetLocalized(nameof(DiagnosticText), value, text => DiagnosticText = text);

    private void SetFoundDevice(LocalizedValue? value) => SetLocalized(nameof(FoundDeviceText), value, text => FoundDeviceText = text);

    /// <summary>Ein Auto-Read des Cube-Reiters läuft höchstens einmal gleichzeitig.</summary>
    private Task? _cubeAutoReadTask;

    private bool _cubeTabReadPending;

    /// <summary>
    /// Parameterloser Konstruktor für die XAML-Designzeit-Vorschau. Erzeugt ausschließlich
    /// In-Memory-Fakes; die echte App nutzt den DI-Konstruktor darunter.
    /// </summary>
    public MainWindowViewModel()
        : this(
            new CubeApplicationService(new DesignTimeGateway(), new DesignTimeStore(), new SystemClock()),
            new SystemClock(),
            discovery: new DesignTimeDiscovery(),
            versionProvider: new AppVersionProvider())
    {
        IsConnected = true;
        UpdateRooms(DesignTimeGateway.CreateCube());
        StatusText = "Vorschau";
    }

    public MainWindowViewModel(
        CubeApplicationService service,
        IClock clock,
        IUiDispatcher? dispatcher = null,
        ICubeDiscovery? discovery = null,
        IAppVersionProvider? versionProvider = null,
        Localizer? localizer = null,
        IAppLog? log = null,
        ILogStore? logStore = null,
        IUrlOpener? urlOpener = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        TemporaryEnd = _clock.Now.AddHours(1);
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        _discovery = discovery ?? new DesignTimeDiscovery();
        _loc = localizer ?? new Localizer("de");
        _labels = new Localizer(_loc.Language);
        _urlOpener = urlOpener ?? new DesktopUrlOpener();
        _logStore = logStore ?? log as ILogStore;
        _log = log ?? _logStore ?? (IAppLog)new NullAppLog();
        if (_logStore is not null)
        {
            // Opt-in: ohne ausdrückliche Aktivierung wird keine Protokolldatei geschrieben.
            _logStore.IsEnabled = false;
        }

        var version = versionProvider ?? new AppVersionProvider();
        AppVersion = version.Version;
        AppVersionText = L("AppVersion", version.Version);
        WindowTitle = $"{L("AppTitle")} {version.Version}";
        statusText = L("StatusDisconnected");
        discoveryText = L("DiscoveryNone");
        profileRoomName = L("NoRoomSelected");
        RoomsStatus = new DataStatusViewModel(_loc);
        DevicesStatus = new DataStatusViewModel(_loc);
        WeekStatus = new DataStatusViewModel(_loc);
        ConfigStatus = new DataStatusViewModel(_loc);
        CubeInfoStatus = new DataStatusViewModel(_loc);
        TimeZoneStatus = new DataStatusViewModel(_loc);
        NtpStatus = new DataStatusViewModel(_loc);
        NetworkStatus = new DataStatusViewModel(_loc);
        DiagnosticsStatus = new DataStatusViewModel(_loc);
        _statuses =
        [
            RoomsStatus, DevicesStatus, WeekStatus, ConfigStatus, CubeInfoStatus,
            TimeZoneStatus, NtpStatus, NetworkStatus, DiagnosticsStatus,
        ];
        foreach (var status in _statuses)
        {
            status.PropertyChanged += OnDataStatusChanged;
        }

        _loc.PropertyChanged += OnLanguageChanged;
        _service.StateChanged += OnStateChanged;
        _service.ConnectionChanged += OnConnectionChanged;
        _service.Warning += OnWarning;
        _service.WarningCode += OnWarningCode;
        _service.StaleChanged += OnStaleChanged;
        _service.AirtimeChanged += OnAirtimeChanged;
        InitializeAdminState();

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var viewModel = new DayProfileViewModel(day, _loc);
            // Ein voller Tag mit 21,0 °C; der letzte Punkt ist 00:00 (= 24:00 im Drahtformat).
            viewModel.Points.Add(new SwitchPointViewModel { UntilText = "00:00", Temperature = 21.0 });
            WeekDays.Add(viewModel);
        }

        SelectedDay = WeekDays.First();
        PropertyChanged += OnEditableValueChanged;
        foreach (var day in WeekDays)
        {
            day.Points.CollectionChanged += OnWeekPointsChanged;
        }

        RefreshStatusBar();
        RefreshDirtyState();
    }

    [ObservableProperty]
    private string windowTitle = "MAX! Cube – Lokale App";

    [ObservableProperty]
    private string appVersion = "unbekannt";

    [ObservableProperty]
    private string appVersionText = "App-Version unbekannt";

    /// <summary>Herkunftsanzeige je Bereich; XAML bindet nur an <c>.Text</c>.</summary>
    public DataStatusViewModel RoomsStatus { get; }

    public DataStatusViewModel DevicesStatus { get; }

    public DataStatusViewModel WeekStatus { get; }

    public DataStatusViewModel ConfigStatus { get; }

    public DataStatusViewModel CubeInfoStatus { get; }

    public DataStatusViewModel TimeZoneStatus { get; }

    public DataStatusViewModel NtpStatus { get; }

    public DataStatusViewModel NetworkStatus { get; }

    public DataStatusViewModel DiagnosticsStatus { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadNetworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendNetworkCommand))]
    private string host = "127.0.0.1";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReadNetworkCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendNetworkCommand))]
    private int port = 62910;

    [ObservableProperty]
    private string statusText = "Getrennt";

    [ObservableProperty]
    private string discoveryText = "Noch keine Suche durchgeführt";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseDiscoveredCubeCommand))]
    private DiscoveredCubeViewModel? selectedDiscoveredCube;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    /// <summary>
    /// Getrennt von StatusText: Eine Warnung („gesendet, Cache nicht aktualisiert“)
    /// darf nicht durch das anschließende „Getrennt“ überschrieben werden.
    /// </summary>
    [ObservableProperty]
    private string warningText = string.Empty;

    /// <summary>Ein success signal for each completed action, independent of StatusText.</summary>
    [ObservableProperty]
    private string successText = string.Empty;

    /// <summary>Sichtbarer Marker für einen veralteten Live-Zustand.</summary>
    [ObservableProperty]
    private bool isStale;

    [ObservableProperty]
    private string profileRoomName = "Kein Raum ausgewählt";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscoverCubesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseDiscoveredCubeCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadLastEndpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseEcoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseComfortCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseTemporaryCommand))]
    private bool isBusy;

    /// <summary>Kontext des laufenden Vorgangs für das modale „Beschäftigt“-Overlay.</summary>
    [ObservableProperty]
    private string busyText = "";

    /// <summary>Vorschau eines riskanten Schreibbefehls vor dem eigentlichen Senden.</summary>
    [ObservableProperty]
    private string writePreviewText = "";

    [ObservableProperty]
    private bool isWritePreviewVisible;

    /// <summary>Sichtbarer Hinweis auf ungespeicherte Bearbeitungen.</summary>
    [ObservableProperty]
    private string dirtyText = "";

    [ObservableProperty]
    private bool hasDirty;

    /// <summary>Begründung, warum eine Schreibaktion gerade gesperrt ist.</summary>
    [ObservableProperty]
    private string weekWriteHint = "";

    [ObservableProperty]
    private string configWriteHint = "";

    [ObservableProperty]
    private string houseWriteHint = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseEcoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseComfortCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseTemporaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(WakeUpRoomCommand))]
    private bool isConnected;

    [ObservableProperty]
    private double requestedTemperature = 21.0;

    /// <summary>
    /// Freie Endzeit für Temporary. Der Startwert kommt aus der injizierten Clock, damit
    /// Tests und Designzeit-Vorschau deterministisch bleiben (v0.8.0).
    /// </summary>
    [ObservableProperty]
    private DateTimeOffset temporaryEnd;

    /// <summary>Datumsteil für den DatePicker; die Zeit bleibt dabei erhalten.</summary>
    public DateTime? TemporaryEndDate
    {
        get => TemporaryEnd.DateTime.Date;
        set
        {
            var date = (value ?? TemporaryEnd.DateTime).Date;
            TemporaryEnd = new DateTimeOffset(date.Add(TemporaryEnd.TimeOfDay), TemporaryEnd.Offset);
        }
    }

    /// <summary>Zeitteil für den TimePicker; das Datum bleibt dabei erhalten.</summary>
    public TimeSpan? TemporaryEndTime
    {
        get => TemporaryEnd.TimeOfDay;
        set
        {
            var time = value ?? TemporaryEnd.TimeOfDay;
            TemporaryEnd = new DateTimeOffset(TemporaryEnd.DateTime.Date.Add(time), TemporaryEnd.Offset);
        }
    }

    partial void OnTemporaryEndChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TemporaryEndDate));
        OnPropertyChanged(nameof(TemporaryEndTime));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyTemperatureCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetEcoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetComfortCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetTemporaryCommand))]
    private RoomViewModel? selectedRoom;

    /// <summary>Tablokale Auswahl des Wochenprogramm-Reiters (v0.8.0).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWeekProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyDayToWeekCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyWeekToSelectedRoomsCommand))]
    private RoomViewModel? selectedWeekRoom;

    /// <summary>Tablokale Auswahl des Konfigurations- und Wartungs-Reiters (v0.8.0).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyRoomConfigurationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyBoostAndDecalcificationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyValveOffsetCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyConfigurationToSelectedRoomsCommand))]
    [NotifyCanExecuteChangedFor(nameof(WakeUpRoomCommand))]
    private RoomViewModel? selectedConfigRoom;

    /// <summary>Tablokale Auswahl des Geräte-/Struktur-Reiters (v0.8.0).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRoomUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRoomDownCommand))]
    private RoomViewModel? selectedDevicesRoom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWeekProfileCommand))]
    private DayProfileViewModel selectedDay;

    public ObservableCollection<RoomViewModel> Rooms { get; } = new();

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    public ObservableCollection<DayProfileViewModel> WeekDays { get; } = new();

    public ObservableCollection<string> Audit { get; } = new();

    public ObservableCollection<DiscoveredCubeViewModel> DiscoveredCubes { get; } = new();

    private bool CanConnect => !IsBusy && IsValidIPv4(Host) && Port is >= 1 and <= 65535;

    private bool CanDiscover => !IsBusy;

    private bool CanUseDiscoveredCube => !IsBusy && SelectedDiscoveredCube is not null;

    private bool CanLoadLastEndpoint => !IsBusy;

    /// <summary>
    /// Online ist nur eine echte Live-Lesung schreibberechtigt; der Cache darf nie
    /// über eine bestehende Verbindung auf den Cube zurückgeschrieben werden (QA 0.6.0).
    /// </summary>
    private bool CanWriteFrom(DataStatusViewModel status)
        => status.State == DataReadState.Live
            || (_service.IsOffline && status.State == DataReadState.Cached);

    private bool CanControlHouse
        => !IsBusy && IsConnected && !IsStale && CanWriteFrom(RoomsStatus);

    /// <summary>
    /// Senden setzt eine echte Lesung voraus: kein Platzhalter, kein Cache, kein veralteter
    /// Zustand. Der Service sperrt Offline zusätzlich (Gremium 0.5.0, QA).
    /// </summary>
    private bool CanEditRoom
        => IsConnected && !IsStale && CanWriteFrom(RoomsStatus) && SelectedRoom is not null;

    /// <summary>
    /// Strenges IPv4-Format: genau vier Oktette mit je 0..255. `IPAddress.TryParse`
    /// würde auch Kurzformen wie `127.1` akzeptieren; die manuelle Eingabe soll aber
    /// wie im Original vier Oktettfelder prüfen (index.html:936-975).
    /// </summary>
    private static bool IsValidIPv4(string host)
    {
        var octets = host.Split('.');
        return octets.Length == 4 && octets.All(octet =>
            octet.Length > 0 &&
            byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    /// <summary>
    /// M-F: Senden nur, wenn der gewählte Wochentag dieses Raums tatsächlich vom Cube
    /// gelesen wurde. Ohne diese Sperre würde der Default-Tag das echte Programm überschreiben.
    /// </summary>
    private bool CanSaveWeekProfile
    {
        get
        {
            if (!IsConnected || IsStale || !CanWriteFrom(RoomsStatus)
                || SelectedWeekRoom is null || !SelectedDay.WasReadFromCube)
            {
                return false;
            }

            try
            {
                return _service.HasWeekProfile(SelectedWeekRoom.Id, SelectedDay.Day);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or MaxCube.Domain.Exceptions.DomainValidationException)
            {
                return false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var endpoint = new CubeEndpoint(Host, Port);
        await RunAsync(async token =>
        {
            var cube = await _service.ConnectAsync(endpoint, token);
            IsConnected = true;
            SetStatus(L("StatusConnected", cube.SerialNumber, cube.FirmwareVersion));
            await RefreshAuditAsync();
        }, L("ErrConnect"), L("OkConnect"),
           errorHint: exception => ConnectErrorHint(exception, endpoint.Host, endpoint.Port));

        // Wurde der Cube-Reiter schon geöffnet, liest die Hybrid-Regel jetzt automatisch
        // nach; vorher war dort nur der Platzhalter „nicht gelesen“ sichtbar.
        if (SelectedTabIndex == TabIndex.Cube)
        {
            await RefreshCubeTabAsync();
        }
    }

    /// <summary>
    /// Hybrid-Regel (Gremium 0.5.0): Beim Aktivieren des Cube-Reiters wird gelesen, wenn
    /// eine Verbindung besteht. Das ist ausdrücklich erlaubt; beim Start oder Verbinden
    /// ohne geöffneten Reiter bleibt der Bereich auf „nicht vom Cube gelesen“.
    /// </summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == TabIndex.Cube)
        {
            _ = RefreshCubeTabAsync();
        }
    }

    /// <summary>
    /// Liest den Cube-Reiter leise (kein modales „Beschäftigt“) und markiert jeden
    /// Abschnitt einzeln. Ein fehlgeschlagener Abschnitt sperrt nur seine eigenen
    /// Schreibbefehle; Räume/Konfiguration bleiben vom Basis-Reload abgedeckt.
    /// </summary>
    private async Task RefreshCubeTabAsync()
    {
        if (!IsConnected || _service.Current is null)
        {
            CubeInfoStatus.Reset();
            return;
        }

        if (_cubeAutoReadTask is { IsCompleted: false })
        {
            return;
        }

        _cubeAutoReadTask = RefreshCubeTabCoreAsync();
        // Fortsetzungen dürfen nie auf dem Threadpool landen: hier werden UI-Properties aktualisiert.
        await _cubeAutoReadTask;
    }

    private async Task RefreshCubeTabCoreAsync()
    {
        if (IsBusy)
        {
            _cubeTabReadPending = true;
            return;
        }

        await RunAsync(async token =>
        {
            _log.Write("INFO", "Cube-Reiter liest Zustand automatisch.");
            await _service.ReloadAsync(token);
            var at = DisplayTime;
            var state = DisplayState;
            RoomsStatus.Update(state, at);
            DevicesStatus.Update(state, at);
            ConfigStatus.Update(state, at);
            CubeInfoStatus.Update(state, at);
            IsConnected = true;

            if (_service.SupportsAdministration)
            {
                await TryReadAsync(TimeZoneStatus, ReadTimeZoneCoreAsync, token);
                await TryReadAsync(NtpStatus, ReadNtpCoreAsync, token);
                if (_service.SupportsNetworkConfiguration)
                {
                    await TryReadAsync(NetworkStatus, ReadNetworkCoreAsync, token);
                }
            }
        }, L("ErrReload"), quiet: true, status: CubeInfoStatus);
    }

    /// <summary>Ein Abschnitt darf einzeln scheitern, ohne die anderen zu blockieren.</summary>
    private async Task TryReadAsync(DataStatusViewModel status, Func<CancellationToken, Task> read, CancellationToken token)
    {
        OnUiThread(status.MarkLoading);
        try
        {
            await read(token);
        }
        catch (Exception exception)
        {
            // Auch die Fehler_markierung gehört auf den UI-Thread; ein MarkFailed aus dem
            // Threadpool löste 0.6.0 „Call from invalid thread" aus (Kundenprotokoll 09:41).
            OnUiThread(() => status.MarkFailed(exception.Message));
        }
    }

    /// <summary>Benutzerinitiierte UDP-Suche; kein Broadcast beim App-Start.</summary>
    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverCubesAsync()
    {
        await RunAsync(async token =>
        {
            var cubes = await _discovery.DiscoverAsync(DiscoveryDuration, token);
            foreach (var previous in DiscoveredCubes) previous.Dispose();
            DiscoveredCubes.Clear();
            foreach (var cube in cubes)
            {
                DiscoveredCubes.Add(new DiscoveredCubeViewModel(cube, _loc));
            }

            SelectedDiscoveredCube = DiscoveredCubes.FirstOrDefault();
            SetDiscovery(DiscoveredCubes.Count == 0
                ? L("DiscoveryNoneFound")
                : L("DiscoveryFound", DiscoveredCubes.Count));
        }, L("ErrDiscovery"), L("OkDiscovery"));
    }

    [RelayCommand(CanExecute = nameof(CanLoadLastEndpoint))]
    private async Task LoadLastEndpointAsync()
    {
        await RunAsync(async token =>
        {
            var endpoint = await _service.GetLastEndpointAsync(token);
            if (endpoint is null)
            {
                SetStatus(L("NoLastEndpoint"));
                return;
            }

            Host = endpoint.Host;
            Port = endpoint.Port;
            SetStatus(L("LastEndpointIs", Host, Port));
        }, L("ErrLastEndpoint"));
    }

    [RelayCommand(CanExecute = nameof(CanUseDiscoveredCube))]
    private void UseDiscoveredCube()
    {
        var cube = SelectedDiscoveredCube;
        if (cube is null)
        {
            return;
        }

        Host = cube.IpAddress;
        Port = CubeTcpPort;
        SetStatus(L("CubeSelected", cube.SerialNumber));
        SetWarning(cube.Bootloader ? L("BootloaderWarning") : null);
    }

    /// <summary>
    /// Immer verfügbar: Nach einem Fault muss der Nutzer die Verbindung auch dann
    /// zurücksetzen können, wenn IsConnected bereits false ist.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await RunAsync(async _ =>
        {
            // Trennen ist intern auf < 2 s begrenzt und darf nicht halb abgebrochen werden.
            await _service.DisconnectAsync();
            IsConnected = false;
            SetStatus(L("StatusDisconnected"));
            _log.Write("INFO", L("OkDisconnect"));
            foreach (var room in Rooms) room.Dispose();
            foreach (var device in Devices) device.Dispose();
            Rooms.Clear();
            Devices.Clear();
            SelectedRoom = null;
            SelectedWeekRoom = null;
            SelectedConfigRoom = null;
            SelectedDevicesRoom = null;
            _weekProfileLoadedFor = null;
            SetProfileRoom(L("NoRoomSelected"));
            MarkAllNotRead();
            FirmwareText = string.Empty;
            SetAirtime(null);
            SetDiagnostic(null);
            _cubeTabReadPending = false;
            await RefreshAuditAsync();
        }, L("ErrDisconnect"), L("OkDisconnect"));
    }

    [RelayCommand]
    private async Task LoadCachedAsync()
    {
        await RunAsync(async token =>
        {
            var cube = await _service.LoadCachedAsync(token);
            SetStatus(cube is null ? L("NoCache") : L("OfflineCache"));
            if (cube is not null)
            {
                // UpdateRooms markt Räume/Geräte/Konfiguration als „Cache vom …“; die
                // Aktionswerte (Zeitzone, NTP, Netzwerk) gibt es im Cache nicht.
                UpdateRooms(cube, DataReadState.Cached);
                TimeZoneStatus.Reset();
                NtpStatus.Reset();
                NetworkStatus.Reset();
            }
        }, L("ErrCache"));
    }

    [RelayCommand(CanExecute = nameof(CanEditRoom))]
    private async Task ApplyTemperatureAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        if (BundleTemperatures && _service.SupportsAdministration)
        {
            // Gebündelte Kommandos: der Wert wird gesammelt und nach dem Sammelfenster
            // gemeinsam gesendet (indexController.js:119-121,187-217).
            try
            {
                _service.QueueRoomTemperature(room.Id, new Temperature(RequestedTemperature));
                SetError(null);
                SetSuccess(L("OkTemperatureQueued", (int)CubeApplicationService.BundleWindow.TotalSeconds));
                _log.Write("INFO", SuccessText);
            }
            catch (Exception exception)
            {
                SetError(L(localizer => $"{localizer.Get("ErrTemperature")}: {exception.Message}"));
                _log.Write("ERROR", $"{ErrorMessage} ({exception.GetType().Name})");
            }

            return;
        }

        await RunAsync(async token =>
        {
            // Der gesendete Wert bleibt im Eingabefeld. `room` ist das alte ViewModel,
            // denn StateChanged erzeugt neue RoomViewModels (Sonnet, M-D).
            await _service.SetRoomTemperatureAsync(room.Id, new Temperature(RequestedTemperature), token);
            await RefreshAuditAsync();
        }, L("ErrTemperature"), L("OkTemperature"));
    }

    [RelayCommand(CanExecute = nameof(CanEditRoom))]
    private async Task SetAutoAsync()
    {
        var room = SelectedRoom;
        if (room is not null)
        {
            await SetModeAsync(room.Id, RoomControlMode.Auto, null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditRoom))]
    private async Task SetEcoAsync()
    {
        var room = SelectedRoom;
        if (room is not null)
        {
            await RunAsync(async token =>
            {
                await _service.ApplyEcoAsync(room.Id, token);
                await RefreshAuditAsync();
            }, L("ErrEco"), L("OkEco"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditRoom))]
    private async Task SetComfortAsync()
    {
        var room = SelectedRoom;
        if (room is not null)
        {
            await RunAsync(async token =>
            {
                await _service.ApplyComfortAsync(room.Id, token);
                await RefreshAuditAsync();
            }, L("ErrComfort"), L("OkComfort"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditRoom))]
    private async Task SetTemporaryAsync()
    {
        var room = SelectedRoom;
        if (room is not null)
        {
            await SetModeAsync(room.Id, RoomControlMode.Temporary, TemporaryEnd);
        }
    }

    /// <summary>Gremium Runde 4: mehrere Räume gemeinsam steuern (P1, leicht).</summary>
    [RelayCommand(CanExecute = nameof(CanControlHouse))]
    private Task SetHouseAutoAsync() => SetHouseModeAsync(HouseMode.Auto);

    [RelayCommand(CanExecute = nameof(CanControlHouse))]
    private Task SetHouseEcoAsync() => SetHouseModeAsync(HouseMode.Eco);

    [RelayCommand(CanExecute = nameof(CanControlHouse))]
    private Task SetHouseComfortAsync() => SetHouseModeAsync(HouseMode.Comfort);

    [RelayCommand(CanExecute = nameof(CanControlHouse))]
    private Task SetHouseTemporaryAsync() => SetHouseModeAsync(HouseMode.Temporary);

    private async Task SetHouseModeAsync(HouseMode mode)
    {
        var rooms = Rooms.Where(room => room.IsHouseSelected).ToArray();
        if (rooms.Length == 0)
        {
            SetSuccess(L("NoRoomsChosen"));
            return;
        }

        await RunAsync(async token =>
        {
            foreach (var room in rooms)
            {
                switch (mode)
                {
                    case HouseMode.Auto:
                        await _service.SetRoomControlModeAsync(
                            room.Id, RoomControlMode.Auto, null, null, token);
                        break;
                    case HouseMode.Eco:
                        await _service.ApplyEcoAsync(room.Id, token);
                        break;
                    case HouseMode.Comfort:
                        await _service.ApplyComfortAsync(room.Id, token);
                        break;
                    case HouseMode.Temporary:
                        await _service.SetRoomControlModeAsync(
                            room.Id, RoomControlMode.Temporary, new Temperature(RequestedTemperature),
                            TemporaryEnd, token);
                        break;
                }
            }

            await RefreshAuditAsync();
        }, L(localizer => localizer.Format("ErrHouseMode", localizer.Get(EnumLabels.Key(mode)))),
           L(localizer => localizer.Format("OkHouseMode", localizer.Get(EnumLabels.Key(mode)))));
    }

    /// <summary>
    /// Gremium Runde 4: Tag auf alle Tage kopieren, für die der Cube ein Profil geliefert
    /// hat. Days ohne Cube-Profil bleiben gesperrt (M-F/Gremium Runde 2).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveWeekProfile))]
    private async Task CopyDayToWeekAsync()
    {
        var room = SelectedWeekRoom;
        if (room is null)
        {
            return;
        }

        DayProfile profile;
        try
        {
            profile = SelectedDay.ToProfile();
        }
        catch (Exception exception)
        {
            SetLocalizedProfileError(exception, "ErrCopyProfile");
            return;
        }

        var days = Enum.GetValues<DayOfWeek>()
            .Where(day => _service.HasWeekProfile(room.Id, day))
            .ToArray();
        if (days.Length == 0)
        {
            SetSuccess(L("NoLoadedProfiles"));
            return;
        }

        await RunAsync(async token =>
        {
            foreach (var day in days)
            {
                await _service.SetWeekDayProfileAsync(room.Id, profile with { Day = day }, token);
            }

            await RefreshAuditAsync();
            MarkWeekSaved();
        }, L("ErrCopyProfile"), L("OkCopyProfile", days.Length),
            previewText: PreviewWeekCopy(days.Length), contextText: L("CopyDay").Append(" …"));
    }

    [RelayCommand(CanExecute = nameof(CanSaveWeekProfile))]
    private async Task SaveWeekProfileAsync()
    {
        var room = SelectedWeekRoom;
        if (room is null)
        {
            return;
        }

        DayProfile profile;
        try
        {
            profile = SelectedDay.ToProfile();
        }
        catch (Exception exception)
        {
            SetLocalizedProfileError(exception, "ErrSendProfile");
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetWeekDayProfileAsync(room.Id, profile, token);
            await RefreshAuditAsync();
            MarkWeekSaved();
        }, L("ErrSendProfile"), L("OkSendProfile"),
            previewText: PreviewWeek(profile, L("SendProfile")), contextText: L("SendProfile").Append(" …"));
    }

    [RelayCommand]
    private void AddSwitchPoint() => SelectedDay.AddPoint();

    /// <summary>Drag-and-drop: Zielzeit aus der Tagesleiste auf den 5-Minuten-Raster schnappen.</summary>
    public bool MoveSwitchPoint(SwitchPointViewModel point, TimeOnly until)
        => SelectedDay.TryMovePoint(point, until);

    [RelayCommand]
    private void RemoveSwitchPoint(SwitchPointViewModel? point)
    {
        if (point is not null)
        {
            SelectedDay.RemovePoint(point);
        }
    }

    private static string FormatNumber(double value)
        => value.ToString("0.0", CultureInfo.InvariantCulture);

    // --- Dirty-Schutz und verständliche Sperrhinweise (0.6.2) -------------------------

    private void OnEditableValueChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not null && ConfigSignatureProperties.Contains(args.PropertyName))
        {
            RefreshDirtyState();
        }
    }

    private void OnWeekPointsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (SwitchPointViewModel point in args.OldItems)
            {
                point.PropertyChanged -= OnWeekPointChanged;
            }
        }

        if (args.NewItems is not null)
        {
            foreach (SwitchPointViewModel point in args.NewItems)
            {
                point.PropertyChanged += OnWeekPointChanged;
            }
        }

        RefreshDirtyState();
    }

    private void OnWeekPointChanged(object? sender, PropertyChangedEventArgs args)
        => RefreshDirtyState();

    private string WeekSignature()
        => string.Join("|", WeekDays.Select(day => string.Join(",",
            day.Points.Select(point => $"{point.UntilText}={FormatNumber(point.Temperature)}"))));

    private string ConfigSignature()
        => string.Join("|",
            FormatNumber(ConfigComfort), FormatNumber(ConfigEco), FormatNumber(ConfigMaximum),
            FormatNumber(ConfigMinimum), FormatNumber(ConfigWindowTemperature), ConfigWindowMinutes,
            FormatNumber(ConfigOffset), BoostMinutes, BoostValveAngle, DecalcificationDay,
            DecalcificationHour, ValveOffsetPercent);

    /// <summary>Nach Laden oder erfolgreichem Senden: current state becomes the saved baseline.</summary>
    internal void MarkWeekSaved()
    {
        _savedWeekRoomId = SelectedWeekRoom?.Id;
        _savedWeekSignature = WeekSignature();
        RefreshDirtyState();
    }

    internal void MarkConfigurationSaved()
    {
        _savedConfigRoomId = SelectedConfigRoom?.Id;
        _savedConfigSignature = ConfigSignature();
        RefreshDirtyState();
    }

    private void RefreshDirtyState()
    {
        var weekDirty = SelectedWeekRoom is not null && _savedWeekRoomId == SelectedWeekRoom.Id
            && _savedWeekSignature is not null
            && !string.Equals(_savedWeekSignature, WeekSignature(), StringComparison.Ordinal);
        var configDirty = SelectedConfigRoom is not null && _savedConfigRoomId == SelectedConfigRoom.Id
            && _savedConfigSignature is not null
            && !string.Equals(_savedConfigSignature, ConfigSignature(), StringComparison.Ordinal);

        HasDirty = weekDirty || configDirty;
        SetLocalized(nameof(DirtyText), (weekDirty, configDirty) switch
        {
            (true, true) => L("DirtyBoth"),
            (true, false) => L("DirtyWeek"),
            (false, true) => L("DirtyConfig"),
            _ => null,
        }, text => DirtyText = text);
    }

    /// <summary>Ein Grund statt einer stumm deaktivierten Schaltfläche.</summary>
    private string WriteHint(
        bool needsRoom, bool needsLoadedWeek, bool needsConfigStatus, RoomViewModel? room)
    {
        if (IsBusy)
        {
            return L("WriteBlockedBusy");
        }

        if (!IsConnected)
        {
            return L("WriteBlockedOffline");
        }

        if (IsStale)
        {
            return L("WriteBlockedStale");
        }

        if (!CanWriteFrom(RoomsStatus) || (needsConfigStatus && !CanWriteFrom(ConfigStatus)))
        {
            return L("WriteBlockedNotRead");
        }

        if (needsRoom && room is null)
        {
            return L("WriteBlockedNoRoom");
        }

        if (needsLoadedWeek && (room is null || !SelectedDay.WasReadFromCube))
        {
            return L("WriteBlockedWeekNotLoaded");
        }

        return L("WriteReady");
    }

    internal void RefreshWriteHints()
    {
        WeekWriteHint = WriteHint(
            needsRoom: true, needsLoadedWeek: true, needsConfigStatus: false, SelectedWeekRoom);
        ConfigWriteHint = !SupportsAdministration
            ? L("WriteBlockedUnsupported")
            : WriteHint(needsRoom: true, needsLoadedWeek: false, needsConfigStatus: true, SelectedConfigRoom);
        HouseWriteHint = Rooms.Any(room => room.IsHouseSelected)
            ? WriteHint(needsRoom: false, needsLoadedWeek: false, needsConfigStatus: false, room: null)
            : L("NoRoomsChosen");
    }

    private LocalizedValue PreviewWeek(DayProfile profile, LocalizedValue action)
        => L(localizer => localizer.Format("PreviewWeek", action.In(localizer).Current, SelectedWeekRoom!.Name,
            localizer.Get(EnumLabels.Key(profile.Day)), profile.Points.Count,
            string.Join(", ", profile.Points.Select(point =>
                $"{point.Until:HH:mm}={FormatNumber(point.Temperature.Value)}"))));

    private LocalizedValue PreviewWeekCopy(int count)
        => L("PreviewWeekCopy", count, SelectedWeekRoom!.Name);

    private LocalizedValue PreviewConfiguration(LocalizedValue action)
        => L(localizer => localizer.Format("PreviewConfig", action.In(localizer).Current, SelectedConfigRoom!.Name,
            FormatNumber(ConfigComfort), FormatNumber(ConfigEco), FormatNumber(ConfigOffset)));

    private LocalizedValue PreviewConfigurationCopy(int count)
        => L("PreviewConfigCopy", count, SelectedConfigRoom!.Name, FormatNumber(ConfigComfort));

    private async Task SetModeAsync(int roomId, RoomControlMode mode, DateTimeOffset? until)
    {
        await RunAsync(async token =>
        {
            await _service.SetRoomControlModeAsync(roomId, mode, new Temperature(RequestedTemperature), until, token);
            await RefreshAuditAsync();
        }, L(localizer => localizer.Format("ErrMode", localizer.Get(EnumLabels.Key(mode)))),
           L(localizer => localizer.Format("OkMode", localizer.Get(EnumLabels.Key(mode)))));
    }

    /// <summary>
    /// 0.6.6: Bekannte Verbindungsursachen als vollständig lokalisierten Dialogtext. Die rohe
    /// Ausnahme bleibt im Protokoll und im Datenstatus; die UI zeigt einen Katalogtext ohne
    /// englische Ausnahmebrocken. Alle Texte sind sprachwechselfähig (sechs Sprachen).
    /// </summary>
    private LocalizedValue? ConnectErrorHint(Exception exception, string host, int port)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrEmpty(current.Message))
            {
                messages.Add(current.Message);
            }
        }

        var text = string.Join(" ", messages);
        if (text.Contains("Timed out connecting to the cube", StringComparison.OrdinalIgnoreCase)
            || messages.Any(message => message.Contains("timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return L("ErrConnectTimeout");
        }

        if (text.Contains("Cannot connect to cube at", StringComparison.OrdinalIgnoreCase))
        {
            return L("ErrConnectRefused", host, port);
        }

        if (text.Contains("No Incoming Hello", StringComparison.OrdinalIgnoreCase))
        {
            return L("ErrConnectHello");
        }

        if (text.Contains("H: line", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Malformed Hello", StringComparison.OrdinalIgnoreCase))
        {
            return L("ErrConnectMalformedHello");
        }

        if (text.Contains("outside the supported range", StringComparison.OrdinalIgnoreCase))
        {
            return L("ErrConnectFirmware");
        }

        if (text.Contains("Already connected. Disconnect first", StringComparison.OrdinalIgnoreCase))
        {
            return L("ErrConnectAlready");
        }

        return null;
    }

    /// <summary>
    /// Führt eine Aktion mit eigenem CTS aus. Der Abbrechen-Button wirkt auf genau diese
    /// Operation; eine abgebrochene Aktion ist kein Fehler (Gremium Runde 1, N7).
    /// <para/>
    /// <paramref name="quiet"/> kennzeichnet das automatische Nachlesen beim Reiterwechsel:
    /// kein modales „Beschäftigt“, kein Fehlerdialog – der Bereich zeigt stattdessen seinen
    /// eigenen Status. <paramref name="status"/> erhält Loading/Failed/Restore.
    /// <paramref name="errorHint"/> liefert für bekannte Verbindungsfehler einen lokalisierten
    /// Dialogtext; ohne Treffer bleibt der bisherige Code-/Generic-Hinweis maßgeblich.
    /// </summary>
    private async Task RunAsync(
        Func<CancellationToken, Task> action, LocalizedValue errorPrefix, LocalizedValue? successText = null,
        bool quiet = false, DataStatusViewModel? status = null, LocalizedValue? previewText = null,
        LocalizedValue? contextText = null, Func<Exception, LocalizedValue?>? errorHint = null)
    {
        if (IsBusy)
        {
            return;
        }

        // 0.6.2: Riskante Schreibbefehle werden nicht sofort gesendet. Die Aktion liegt
        // bis zur ausdrücklichen Bestätigung lokal im ViewModel; ein Abbruch sendet nichts.
        if (!quiet && !string.IsNullOrWhiteSpace(previewText?.Current))
        {
            _pendingWrite = new PendingWrite(action, errorPrefix, successText, status);
            SetPreview(previewText);
            IsWritePreviewVisible = true;
            ConfirmWriteCommand.NotifyCanExecuteChanged();
            CancelWriteCommand.NotifyCanExecuteChanged();
            return;
        }

        using var cts = new CancellationTokenSource();
        OnUiThread(() =>
        {
            if (!quiet)
            {
                _actionCts = cts;
                IsBusy = true;
                SetBusy(string.IsNullOrWhiteSpace(contextText?.Current) ? L("Busy") : contextText);
                SetError(null);
                SetSuccess(null);
                // Eine alte Warnung gehört zur vorherigen Aktion und darf nicht dauerhaft
                // stehen bleiben (Sonnet N10).
                SetWarning(null);
            }
            else if (!string.IsNullOrWhiteSpace(contextText?.Current))
            {
                SetBusy(contextText);
            }

            status?.MarkLoading();
        });

        try
        {
            await action(cts.Token);
            var cancelled = cts.IsCancellationRequested;
            var result = cancelled ? L("Cancelled") : successText ?? L("OkAction");
            OnUiThread(() =>
            {
                if (!quiet)
                {
                    SetSuccess(result);
                }

                // 0.6.1: Nur der Erfolgstext, nie der Fehlerprefix. 0.6.0 schrieb
                // „Temperatur konnte nicht gesendet werden: Temperatur gesendet."
                _log.Write("INFO", result);
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            OnUiThread(() =>
            {
                status?.Restore();
                if (!quiet)
                {
                    SetSuccess(L("Cancelled"));
                }

                _log.Write("INFO", L("Cancelled"));
            });
        }
        catch (Exception exception)
        {
            // Die rohe Ausnahme bleibt im Log; der sichtbare Dialog bekommt seit 0.6.6
            // zuerst einen lokalisierten Ursachenhinweis (Verbinden) und sonst den
            // vollständig lokalisierten Code-/Generic-Hinweis (0.6.4/0.6.5).
            var code = ErrorCatalog.FindFirstCode(exception.Message);
            var message = errorHint?.Invoke(exception)
                ?? (code is null
                    ? L(localizer => $"{errorPrefix.In(localizer).Current}: {localizer.Get("ErrorGeneric")}")
                    : L("ErrorWithCode", errorPrefix, code));
            OnUiThread(() =>
            {
                if (!quiet)
                {
                    SetError(message);
                }

                // Der Ausnahmetyp hilft beim Support ohne Nutzdaten oder Zugangsdaten.
                _log.Write(quiet ? "WARN" : "ERROR", $"{message} ({exception.GetType().Name})");
                status?.MarkFailed(exception.Message);
            });
        }
        finally
        {
            OnUiThread(() =>
            {
                if (!quiet)
                {
                    // Der Abbruch-Hinweis gehört nur zur laufenden Aktion (Gremium Runde 2).
                    if (StatusText == L("StatusCancelling").Current)
                    {
                        SetStatus(_statusBeforeCancel?.In(_loc) ?? L("StatusDisconnected"));
                    }

                    _statusBeforeCancel = null;
                    _actionCts = null;
                    IsBusy = false;
                }

                SetBusy(null);

                if (_cubeTabReadPending && !IsBusy && SelectedTabIndex == TabIndex.Cube)
                {
                    _cubeTabReadPending = false;
                    _ = RefreshCubeTabAsync();
                }
            });
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            return;
        }

        if (StatusText != L("StatusCancelling").Current)
        {
            _statusBeforeCancel = _localizedBindings.TryGetValue(nameof(StatusText), out var binding)
                ? binding.Value
                : new LocalizedValue(StatusText);
        }

        // Gremium Runde 3: Erst den sichtbaren Hinweis setzen, dann abbrechen.
        // Sonst kann die Aktion zwischen Cancel und Statuszuweisung enden; der
        // finally-Restore läuft dann ins Leere und „Abbrechen …" bleibt stehen.
        SetStatus(L("StatusCancelling"));
        _actionCts?.Cancel();
        _service.CancelActiveOperation();
    }

    private bool HasPendingWrite => _pendingWrite is not null;

    /// <summary>Führt genau die zuvor angezeigte Aktion aus; ohne Klick kein Sendevorgang.</summary>
    [RelayCommand(CanExecute = nameof(HasPendingWrite))]
    private async Task ConfirmWriteAsync()
    {
        var pending = _pendingWrite;
        if (pending is null)
        {
            return;
        }

        _pendingWrite = null;
        IsWritePreviewVisible = false;
        SetPreview(null);
        ConfirmWriteCommand.NotifyCanExecuteChanged();
        CancelWriteCommand.NotifyCanExecuteChanged();
        await RunAsync(pending.Action, pending.ErrorPrefix, pending.SuccessText, status: pending.Status);
    }

    /// <summary>Verwirft eine Vorschau; der Cube bleibt unverändert.</summary>
    [RelayCommand(CanExecute = nameof(HasPendingWrite))]
    private void CancelWrite()
    {
        _pendingWrite = null;
        IsWritePreviewVisible = false;
        SetPreview(null);
        ConfirmWriteCommand.NotifyCanExecuteChanged();
        CancelWriteCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshAuditAsync()
    {
        var entries = await _service.ReadAuditAsync();
        // Die Audit-Liste ist an die View gebunden; auch bei einem Hintergrundaufruf
        // dürfen Clear/Add niemals außerhalb des UI-Threads laufen (0.6.1).
        OnUiThread(() =>
        {
            Audit.Clear();
            foreach (var entry in entries.TakeLast(100))
            {
                Audit.Add(entry);
            }
        });
    }

    private void OnStateChanged(object? sender, Cube cube) => OnUiThread(() => UpdateRooms(cube));

    private void OnWarning(object? sender, string message) => _log.Write("WARN", message);

    private void OnWarningCode(object? sender, ServiceWarning warning) => OnUiThread(() =>
        SetWarning(warning.Parameter is null
            ? L(warning.Code)
            : L(warning.Code, warning.Parameter)));

    private void OnStaleChanged(object? sender, bool stale) => OnUiThread(() =>
    {
        IsStale = stale;
        MarkStatusesStale(stale);
        // Gremium Runde 2, Produkt/UX: Der Reconnect ist auch in der Statusleiste
        // sichtbar, nicht nur als Warnung und als Stale-Marker.
        if (stale)
        {
            SetStatus(L("StatusReconnecting"));
            _log.Write("WARN", L("StatusReconnecting"));
        }
        else if (StatusText == L("StatusReconnecting").Current)
        {
            SetStatus(IsConnected ? L("StatusRestored") : L("StatusDisconnected"));
            _log.Write("INFO", L("StatusRestored"));
        }
    });

    private void OnConnectionChanged(object? sender, CubeConnectionState state)
    {
        if (state is not (CubeConnectionState.Faulted or CubeConnectionState.Connected))
        {
            return;
        }

        OnUiThread(() =>
        {
            if (state == CubeConnectionState.Faulted)
            {
                IsConnected = false;
                SetStatus(L("StatusInterrupted"));
                _log.Write("ERROR", L("StatusInterrupted"));
                MarkStatusesStale(true);
                return;
            }

            // Gremium Runde 3, Prüfung: Der Service meldet auch den automatischen
            // Reconnect. Ohne diesen Zweig blieb IsConnected false, die Aktionen
            // waren gesperrt und ein erneuter Connect lief in „Already connected“.
            var wasConnected = IsConnected;
            IsConnected = true;
            if (!wasConnected)
            {
                SetStatus(L("StatusRestored"));
            }

            MarkStatusesStale(false);
        });
    }

    /// <summary>
    /// Service-Events und Fortsetzungen können aus dem Threadpool kommen. ObservableCollections
    /// und gebundene Properties dürfen nur auf dem UI-Thread verändert werden; Avalonia meldet
    /// einen Verstoß mit „Call from invalid thread". Alle UI-Mutationen laufen hier hindurch.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    /// <summary>Alle Bereiche wieder als „nicht gelesen“ kennzeichnen (Trennen/Fehler).</summary>
    private void MarkAllNotRead()
    {
        foreach (var status in _statuses)
        {
            status.Reset();
        }

        _cubeAutoReadTask = null;
        RefreshAdminCommands();
        SaveWeekProfileCommand.NotifyCanExecuteChanged();
    }

    private void MarkStatusesStale(bool stale)
    {
        var restored = _service.IsOffline ? DataReadState.Cached : DataReadState.Live;
        foreach (var status in _statuses)
        {
            status.MarkStale(stale, restored);
        }
    }

    private DataReadState DisplayState => _service.IsOffline ? DataReadState.Cached : DataReadState.Live;

    private DateTimeOffset DisplayTime => _service.LastReadAt ?? _clock.Now;

    /// <summary>Statusänderungen sperren oder entsperren die zugehörigen Schreibbefehle.</summary>
    private void OnDataStatusChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DataStatusViewModel.State) or nameof(DataStatusViewModel.HasRead))
        {
            RefreshAdminCommands();
            SaveWeekProfileCommand.NotifyCanExecuteChanged();
            ApplyTemperatureCommand.NotifyCanExecuteChanged();
            SetAutoCommand.NotifyCanExecuteChanged();
            SetEcoCommand.NotifyCanExecuteChanged();
            SetComfortCommand.NotifyCanExecuteChanged();
            SetTemporaryCommand.NotifyCanExecuteChanged();
            RefreshStatusBar();
        }
    }

    private void UpdateRooms(Cube cube, DataReadState? explicitState = null)
    {
        // Kein stiller Default mehr: Herkunft und Zeitpunkt kommen aus dem Service.
        // Ein Cache-Ladevorgang bleibt auch bei bestehender Verbindung Cached.
        var state = explicitState ?? DisplayState;
        var at = DisplayTime;
        RoomsStatus.Update(state, at);
        DevicesStatus.Update(state, at);

        var previousRoomId = SelectedRoom?.Id;
        var previousWeekRoomId = SelectedWeekRoom?.Id;
        var previousConfigRoomId = SelectedConfigRoom?.Id;
        var previousDevicesRoomId = SelectedDevicesRoom?.Id;
        var houseSelection = Rooms.Where(room => room.IsHouseSelected).Select(room => room.Id).ToHashSet();
        foreach (var previous in Rooms) previous.Dispose();
        Rooms.Clear();
        foreach (var room in cube.Rooms)
        {
            var viewModel = new RoomViewModel(room, _loc);
            viewModel.IsHouseSelected = houseSelection.Contains(room.Id);
            Rooms.Add(viewModel);
        }

        var selected = Rooms.FirstOrDefault(room => room.Id == previousRoomId) ?? Rooms.FirstOrDefault();
        var week = Rooms.FirstOrDefault(room => room.Id == previousWeekRoomId) ?? selected;
        var config = Rooms.FirstOrDefault(room => room.Id == previousConfigRoomId) ?? selected;
        var devices = Rooms.FirstOrDefault(room => room.Id == previousDevicesRoomId) ?? selected;
        // Nur bei gleicher, bereits vorhandener Auswahl unterdrücken. Beim ersten Laden
        // und nach Disconnect (previousId == null) muss der Raum-Sollwert übernommen
        // werden (Sonnet N9); ein echter Raumwechsel läuft ohnehin ungesperrt.
        var keepTypedValue = previousRoomId is not null && selected?.Id == previousRoomId;
        _suppressRoomSelectionHandlers = true;
        _suppressRequestedTemperatureSync = keepTypedValue;
        try
        {
            SelectedRoom = selected;
            SelectedWeekRoom = week;
            SelectedConfigRoom = config;
            SelectedDevicesRoom = devices;
        }
        finally
        {
            _suppressRequestedTemperatureSync = false;
            _suppressRoomSelectionHandlers = false;
        }

        if (!keepTypedValue && selected is not null)
        {
            RequestedTemperature = selected.SetPoint;
        }

        LoadWeekProfileFor(cube, week, keepEdited: previousWeekRoomId is not null && week?.Id == previousWeekRoomId);
        RefreshDevices(cube, devices);
        RebuildRoomOptions(cube);
        LoadAdminStateFor(
            cube, config, keepEdited: previousConfigRoomId is not null && config?.Id == previousConfigRoomId);
        if (previousDevicesRoomId is null || devices?.Id != previousDevicesRoomId)
        {
            RenameRoomText = devices?.Name ?? string.Empty;
        }

        UpdateWeekStatus();
        RefreshWriteHints();
        RefreshAdminCommands();
    }

    private void RefreshDevices(Cube cube, RoomViewModel? room)
    {
        foreach (var previous in Devices) previous.Dispose();
        Devices.Clear();
        // Gremium Runde 1: Die Geräteliste gehört zum gewählten Raum, nicht zum ganzen Cube.
        foreach (var device in cube.Devices.Where(device => room is null || device.RoomId == room.Id))
        {
            Devices.Add(new DeviceViewModel(device, _loc));
        }
    }

    private void LoadWeekProfileFor(Cube cube, RoomViewModel? room, bool keepEdited)
    {
        if (room is null)
        {
            SetProfileRoom(L("NoRoomSelected"));
            WeekStatus.Reset();
            MarkWeekSaved();
            return;
        }

        SetProfileRoom(L("ProfileForRoom", room.Id, room.Name));
        if (keepEdited && _weekProfileLoadedFor == room.Id)
        {
            return; // bearbeitete Schaltpunkte eines StateChanged nicht überschreiben
        }

        var profile = cube.GetRoom(room.Id)?.WeekProfile;
        foreach (var day in WeekDays)
        {
            var dayProfile = profile?.GetDay(day.Day);
            if (dayProfile is not null)
            {
                day.Load(dayProfile);
            }
            else
            {
                day.Reset(); // Platzhalter, nicht sendbar
            }
        }

        _weekProfileLoadedFor = room.Id;
        MarkWeekSaved();
        UpdateWeekStatus();
        SaveWeekProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Der gewählte Tag entscheidet, ob das Wochenprofil „gelesen“ ist.</summary>
    private void UpdateWeekStatus()
    {
        if (SelectedWeekRoom is null || !SelectedDay.WasReadFromCube)
        {
            WeekStatus.Reset();
            return;
        }

        WeekStatus.Update(DisplayState, DisplayTime);
    }

    partial void OnSelectedDayChanged(DayProfileViewModel value) => UpdateWeekStatus();

    partial void OnSelectedRoomChanged(RoomViewModel? value)
    {
        // Die Räume-Auswahl steuert nur den Sollwert und die Modus-Schaltflächen.
        // Wochenprofil, Konfiguration und Geräteliste haben seit v0.8.0 eigene Auswahlen.
        if (value is not null && !_suppressRequestedTemperatureSync && !_suppressRoomSelectionHandlers)
        {
            RequestedTemperature = value.SetPoint;
        }
    }

    partial void OnSelectedWeekRoomChanged(RoomViewModel? value)
    {
        if (_suppressRoomSelectionHandlers || _service.Current is not { } cube)
        {
            return;
        }

        LoadWeekProfileFor(cube, value, keepEdited: false);
        UpdateWeekStatus();
        RefreshWriteHints();
        SaveWeekProfileCommand.NotifyCanExecuteChanged();
        CopyDayToWeekCommand.NotifyCanExecuteChanged();
        CopyWeekToSelectedRoomsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConfigRoomChanged(RoomViewModel? value)
    {
        if (_suppressRoomSelectionHandlers || _service.Current is not { } cube)
        {
            return;
        }

        LoadAdminStateFor(cube, value, keepEdited: false);
        RefreshWriteHints();
        RefreshDirtyState();
        RefreshAdminCommands();
    }

    partial void OnSelectedDevicesRoomChanged(RoomViewModel? value)
    {
        if (_suppressRoomSelectionHandlers || _service.Current is not { } cube)
        {
            return;
        }

        RenameRoomText = value?.Name ?? string.Empty;
        RefreshDevices(cube, value);
        RefreshAdminCommands();
    }

    public void Dispose()
    {
        _actionCts?.Cancel();
        _actionCts?.Dispose();
        PropertyChanged -= OnEditableValueChanged;
        foreach (var day in WeekDays)
        {
            day.Points.CollectionChanged -= OnWeekPointsChanged;
            foreach (var point in day.Points)
            {
                point.PropertyChanged -= OnWeekPointChanged;
            }
        }

        _loc.PropertyChanged -= OnLanguageChanged;
        foreach (var day in WeekDays) day.Dispose();
        foreach (var room in Rooms) room.Dispose();
        foreach (var device in Devices) device.Dispose();
        foreach (var discovered in DiscoveredCubes) discovered.Dispose();
        _service.AirtimeChanged -= OnAirtimeChanged;
        _service.StateChanged -= OnStateChanged;
        _service.ConnectionChanged -= OnConnectionChanged;
        _service.Warning -= OnWarning;
        _service.WarningCode -= OnWarningCode;
        _service.StaleChanged -= OnStaleChanged;
        foreach (var status in _statuses)
        {
            status.PropertyChanged -= OnDataStatusChanged;
            status.Dispose();
        }
    }
}
