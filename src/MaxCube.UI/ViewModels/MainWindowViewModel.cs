using System.Collections.ObjectModel;
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

    /// <summary>Zeitbudget des Cube-Finders; entspricht dem Original (cubeScanTime=1800 ms).</summary>
    private static readonly TimeSpan DiscoveryDuration = TimeSpan.FromMilliseconds(1800);

    /// <summary>Port des physischen Cubes; Discovery liefert nur die IP.</summary>
    private const int CubeTcpPort = 62910;

    /// <summary>Ein StateChanged mit demselben Raum darf die getippte Eingabe nicht überschreiben.</summary>
    private bool _suppressRequestedTemperatureSync;

    /// <summary>
    /// Raum, für den das Wochenprofil zuletzt aus dem Cube geladen wurde. Ein StateChanged
    /// mit demselben Raum darf die gerade bearbeiteten Schaltpunkte nicht zurücksetzen.
    /// </summary>
    private int? _weekProfileLoadedFor;

    /// <summary>Aktiver CTS für den sichtbaren Abbrechen-Button.</summary>
    private CancellationTokenSource? _actionCts;
    private string? _statusBeforeCancel;

    /// <summary>Herence und Alter pro Bereich; nie in der Domäne, immer im ViewModel.</summary>
    private readonly DataStatusViewModel[] _statuses;

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
        ILogStore? logStore = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        _discovery = discovery ?? new DesignTimeDiscovery();
        _loc = localizer ?? new Localizer("de");
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
        _service.StaleChanged += OnStaleChanged;
        _service.AirtimeChanged += OnAirtimeChanged;
        InitializeAdminState();

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var viewModel = new DayProfileViewModel(day);
            // Ein voller Tag mit 21,0 °C; der letzte Punkt ist 00:00 (= 24:00 im Drahtformat).
            viewModel.Points.Add(new SwitchPointViewModel { UntilText = "00:00", Temperature = 21.0 });
            WeekDays.Add(viewModel);
        }

        SelectedDay = WeekDays.First();
        RefreshStatusBar();
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
    private string host = "127.0.0.1";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseEcoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseComfortCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetHouseTemporaryCommand))]
    private bool isConnected;

    [ObservableProperty]
    private double requestedTemperature = 21.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyTemperatureCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetAutoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetEcoCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetComfortCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetTemporaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveWeekProfileCommand))]
    private RoomViewModel? selectedRoom;

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
                || SelectedRoom is null || !SelectedDay.WasReadFromCube)
            {
                return false;
            }

            try
            {
                return _service.HasWeekProfile(SelectedRoom.Id, SelectedDay.Day);
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
        await RunAsync(async token =>
        {
            var cube = await _service.ConnectAsync(new CubeEndpoint(Host, Port), token);
            IsConnected = true;
            StatusText = L("StatusConnected", cube.SerialNumber, cube.FirmwareVersion);
            await RefreshAuditAsync();
        }, L("ErrConnect"), L("OkConnect"));

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
        await _cubeAutoReadTask.ConfigureAwait(false);
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
        }, L("ErrReload"), quiet: true, status: CubeInfoStatus).ConfigureAwait(false);
    }

    /// <summary>Ein Abschnitt darf einzeln scheitern, ohne die anderen zu blockieren.</summary>
    private async Task TryReadAsync(DataStatusViewModel status, Func<CancellationToken, Task> read, CancellationToken token)
    {
        status.MarkLoading();
        try
        {
            await read(token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            status.MarkFailed(exception.Message);
        }
    }

    /// <summary>Benutzerinitiierte UDP-Suche; kein Broadcast beim App-Start.</summary>
    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverCubesAsync()
    {
        await RunAsync(async token =>
        {
            var cubes = await _discovery.DiscoverAsync(DiscoveryDuration, token);
            DiscoveredCubes.Clear();
            foreach (var cube in cubes)
            {
                DiscoveredCubes.Add(new DiscoveredCubeViewModel(cube));
            }

            SelectedDiscoveredCube = DiscoveredCubes.FirstOrDefault();
            DiscoveryText = DiscoveredCubes.Count == 0
                ? L("DiscoveryNoneFound")
                : L("DiscoveryFound", DiscoveredCubes.Count);
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
                StatusText = L("NoLastEndpoint");
                return;
            }

            Host = endpoint.Host;
            Port = endpoint.Port;
            StatusText = L("LastEndpointIs", Host, Port);
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
        StatusText = L("CubeSelected", cube.SerialNumber);
        WarningText = cube.Bootloader
            ? L("BootloaderWarning")
            : string.Empty;
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
            StatusText = L("StatusDisconnected");
            _log.Write("INFO", "Verbindung zum Cube getrennt.");
            Rooms.Clear();
            Devices.Clear();
            SelectedRoom = null;
            _weekProfileLoadedFor = null;
            ProfileRoomName = L("NoRoomSelected");
            MarkAllNotRead();
            FirmwareText = string.Empty;
            AirtimeText = string.Empty;
            DiagnosticText = string.Empty;
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
            StatusText = cube is null ? L("NoCache") : L("OfflineCache");
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
                ErrorMessage = string.Empty;
                SuccessText = L("OkTemperatureQueued", (int)CubeApplicationService.BundleWindow.TotalSeconds);
            }
            catch (Exception exception)
            {
                ErrorMessage = $"{L("ErrTemperature")}: {exception.Message}";
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
            await SetModeAsync(room.Id, RoomControlMode.Temporary, _clock.Now.AddHours(1));
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
            SuccessText = L("NoRoomsChosen");
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
                            room.Id, RoomControlMode.Auto, null, null, token).ConfigureAwait(false);
                        break;
                    case HouseMode.Eco:
                        await _service.ApplyEcoAsync(room.Id, token).ConfigureAwait(false);
                        break;
                    case HouseMode.Comfort:
                        await _service.ApplyComfortAsync(room.Id, token).ConfigureAwait(false);
                        break;
                    case HouseMode.Temporary:
                        await _service.SetRoomControlModeAsync(
                            room.Id, RoomControlMode.Temporary, new Temperature(RequestedTemperature),
                            _clock.Now.AddHours(1), token).ConfigureAwait(false);
                        break;
                }
            }

            await RefreshAuditAsync();
        }, L("ErrHouseMode", mode), L("OkHouseMode", mode));
    }

    /// <summary>
    /// Gremium Runde 4: Tag auf alle Tage kopieren, für die der Cube ein Profil geliefert
    /// hat. Days ohne Cube-Profil bleiben gesperrt (M-F/Gremium Runde 2).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveWeekProfile))]
    private async Task CopyDayToWeekAsync()
    {
        var room = SelectedRoom;
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
            ErrorMessage = exception.Message;
            return;
        }

        var days = Enum.GetValues<DayOfWeek>()
            .Where(day => _service.HasWeekProfile(room.Id, day))
            .ToArray();
        if (days.Length == 0)
        {
            SuccessText = L("NoLoadedProfiles");
            return;
        }

        await RunAsync(async token =>
        {
            foreach (var day in days)
            {
                await _service.SetWeekDayProfileAsync(room.Id, profile with { Day = day }, token)
                    .ConfigureAwait(false);
            }

            await RefreshAuditAsync();
        }, L("ErrCopyProfile"), L("OkCopyProfile", days.Length));
    }

    [RelayCommand(CanExecute = nameof(CanSaveWeekProfile))]
    private async Task SaveWeekProfileAsync()
    {
        var room = SelectedRoom;
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
            ErrorMessage = exception.Message;
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetWeekDayProfileAsync(room.Id, profile, token);
            await RefreshAuditAsync();
        }, L("ErrSendProfile"), L("OkSendProfile"));
    }

    [RelayCommand]
    private void AddSwitchPoint() => SelectedDay.AddPoint();

    [RelayCommand]
    private void RemoveSwitchPoint(SwitchPointViewModel? point)
    {
        if (point is not null)
        {
            SelectedDay.RemovePoint(point);
        }
    }

    private async Task SetModeAsync(int roomId, RoomControlMode mode, DateTimeOffset? until)
    {
        await RunAsync(async token =>
        {
            await _service.SetRoomControlModeAsync(roomId, mode, new Temperature(RequestedTemperature), until, token);
            await RefreshAuditAsync();
        }, L("ErrMode", mode), L("OkMode", mode));
    }

    /// <summary>
    /// Führt eine Aktion mit eigenem CTS aus. Der Abbrechen-Button wirkt auf genau diese
    /// Operation; eine abgebrochene Aktion ist kein Fehler (Gremium Runde 1, N7).
    /// <para/>
    /// <paramref name="quiet"/> kennzeichnet das automatische Nachlesen beim Reiterwechsel:
    /// kein modales „Beschäftigt“, kein Fehlerdialog – der Bereich zeigt stattdessen seinen
    /// eigenen Status. <paramref name="status"/> erhält Loading/Failed/Restore.
    /// </summary>
    private async Task RunAsync(
        Func<CancellationToken, Task> action, string errorPrefix, string? successText = null,
        bool quiet = false, DataStatusViewModel? status = null)
    {
        if (IsBusy)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        if (!quiet)
        {
            _actionCts = cts;
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessText = string.Empty;
            // Eine alte Warnung gehört zur vorherigen Aktion und darf nicht dauerhaft
            // stehen bleiben (Sonnet N10).
            WarningText = string.Empty;
        }

        status?.MarkLoading();
        try
        {
            await action(cts.Token);
            var result = cts.IsCancellationRequested ? L("Cancelled") : successText ?? L("OkAction");
            if (!quiet)
            {
                SuccessText = result;
            }

            // INFO deckt jede Aktion ab - das war der grösste Support-Blindfleck (QA 0.6.0).
            _log.Write("INFO", $"{errorPrefix}: {result}");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            status?.Restore();
            if (!quiet)
            {
                SuccessText = L("Cancelled");
            }
        }
        catch (Exception exception)
        {
            if (!quiet)
            {
                ErrorMessage = $"{errorPrefix}: {exception.Message}";
                _log.Write("ERROR", ErrorMessage);
            }
            else
            {
                _log.Write("WARN", $"{errorPrefix}: {exception.Message}");
            }

            status?.MarkFailed(exception.Message);
        }
        finally
        {
            if (!quiet)
            {
                // Der Abbruch-Hinweis gehört nur zur laufenden Aktion (Gremium Runde 2).
                if (StatusText == L("StatusCancelling"))
                {
                    StatusText = _statusBeforeCancel ?? L("StatusDisconnected");
                }

                _statusBeforeCancel = null;
                _actionCts = null;
                IsBusy = false;
            }

            if (_cubeTabReadPending && !IsBusy && SelectedTabIndex == TabIndex.Cube)
            {
                _cubeTabReadPending = false;
                _ = RefreshCubeTabAsync();
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            return;
        }

        if (StatusText != L("StatusCancelling"))
        {
            _statusBeforeCancel = StatusText;
        }

        // Gremium Runde 3: Erst den sichtbaren Hinweis setzen, dann abbrechen.
        // Sonst kann die Aktion zwischen Cancel und Statuszuweisung enden; der
        // finally-Restore läuft dann ins Leere und „Abbrechen …" bleibt stehen.
        StatusText = L("StatusCancelling");
        _actionCts?.Cancel();
        _service.CancelActiveOperation();
    }

    private async Task RefreshAuditAsync()
    {
        var entries = await _service.ReadAuditAsync();
        Audit.Clear();
        foreach (var entry in entries.TakeLast(100))
        {
            Audit.Add(entry);
        }
    }

    private void OnStateChanged(object? sender, Cube cube) => OnUiThread(() => UpdateRooms(cube));

    private void OnWarning(object? sender, string message) => OnUiThread(() =>
    {
        WarningText = message;
        _log.Write("WARN", message);
    });

    private void OnStaleChanged(object? sender, bool stale) => OnUiThread(() =>
    {
        IsStale = stale;
        MarkStatusesStale(stale);
        // Gremium Runde 2, Produkt/UX: Der Reconnect ist auch in der Statusleiste
        // sichtbar, nicht nur als Warnung und als Stale-Marker.
        if (stale)
        {
            StatusText = L("StatusReconnecting");
            _log.Write("WARN", "Veraltete Daten erkannt - automatische Reconnect läuft.");
        }
        else if (StatusText == L("StatusReconnecting"))
        {
            StatusText = IsConnected ? L("StatusRestored") : L("StatusDisconnected");
            _log.Write("INFO", "Verbindung wiederhergestellt.");
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
                StatusText = L("StatusInterrupted");
                _log.Write("ERROR", "Verbindung zum Cube unterbrochen.");
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
                StatusText = L("StatusRestored");
            }

            MarkStatusesStale(false);
        });
    }

    /// <summary>
    /// Die Events kommen nach ConfigureAwait(false) aus dem Threadpool. ObservableCollections
    /// und gebundene Properties dürfen nur auf dem UI-Thread verändert werden.
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

        var previousId = SelectedRoom?.Id;
        var houseSelection = Rooms.Where(room => room.IsHouseSelected).Select(room => room.Id).ToHashSet();
        Rooms.Clear();
        foreach (var room in cube.Rooms)
        {
            var viewModel = new RoomViewModel(room, _loc);
            viewModel.IsHouseSelected = houseSelection.Contains(room.Id);
            Rooms.Add(viewModel);
        }

        var selected = Rooms.FirstOrDefault(room => room.Id == previousId) ?? Rooms.FirstOrDefault();
        // Nur bei gleicher, bereits vorhandener Auswahl unterdrücken. Beim ersten Laden
        // und nach Disconnect (previousId == null) muss der Raum-Sollwert übernommen
        // werden (Sonnet N9); ein echter Raumwechsel läuft ohnehin ungesperrt.
        var keepTypedValue = previousId is not null && selected?.Id == previousId;
        _suppressRequestedTemperatureSync = keepTypedValue;
        try
        {
            SelectedRoom = selected;
        }
        finally
        {
            _suppressRequestedTemperatureSync = false;
        }

        if (!keepTypedValue && selected is not null)
        {
            RequestedTemperature = selected.SetPoint;
        }

        LoadWeekProfileFor(cube, selected, keepEdited: keepTypedValue);
        RefreshDevices(cube, selected);
        RebuildRoomOptions(cube);
        LoadAdminStateFor(cube, selected, keepEdited: keepTypedValue);
    }

    private void RefreshDevices(Cube cube, RoomViewModel? room)
    {
        Devices.Clear();
        // Gremium Runde 1: Die Geräteliste gehört zum gewählten Raum, nicht zum ganzen Cube.
        foreach (var device in cube.Devices.Where(device => room is null || device.RoomId == room.Id))
        {
            Devices.Add(new DeviceViewModel(device));
        }
    }

    private void LoadWeekProfileFor(Cube cube, RoomViewModel? room, bool keepEdited)
    {
        if (room is null)
        {
            ProfileRoomName = L("NoRoomSelected");
            WeekStatus.Reset();
            return;
        }

        ProfileRoomName = L("ProfileForRoom", room.Id, room.Name);
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
        UpdateWeekStatus();
        SaveWeekProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Der gewählte Tag entscheidet, ob das Wochenprofil „gelesen“ ist.</summary>
    private void UpdateWeekStatus()
    {
        if (SelectedRoom is null || !SelectedDay.WasReadFromCube)
        {
            WeekStatus.Reset();
            return;
        }

        WeekStatus.Update(DisplayState, DisplayTime);
    }

    partial void OnSelectedDayChanged(DayProfileViewModel value) => UpdateWeekStatus();

    partial void OnSelectedRoomChanged(RoomViewModel? value)
    {
        if (value is not null && !_suppressRequestedTemperatureSync)
        {
            RequestedTemperature = value.SetPoint;
            if (_service.Current is { } cube)
            {
                LoadWeekProfileFor(cube, value, keepEdited: false);
                // Gremium Runde 3: Ohne diesen Aufruf zeigte die Liste nach einem
                // manuellen Raumwechsel die Geräte des alten Raums, bis der nächste
                // StateChanged die Liste neu füllte.
                RefreshDevices(cube, value);
                LoadAdminStateFor(cube, value, keepEdited: false);
            }
        }

        RefreshAdminCommands();
    }

    public void Dispose()
    {
        _actionCts?.Cancel();
        _actionCts?.Dispose();
        _loc.PropertyChanged -= OnLanguageChanged;
        _service.AirtimeChanged -= OnAirtimeChanged;
        _service.StateChanged -= OnStateChanged;
        _service.ConnectionChanged -= OnConnectionChanged;
        _service.Warning -= OnWarning;
        _service.StaleChanged -= OnStaleChanged;
        foreach (var status in _statuses)
        {
            status.PropertyChanged -= OnDataStatusChanged;
            status.Dispose();
        }
    }
}
