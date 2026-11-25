using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.UI.Localization;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.ViewModels;

/// <summary>Ein Eintrag der Raumauswahl; Id 0 steht für das Haus (Geräte ohne Raum).</summary>
public sealed record RoomOption(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Tab-Positionen von <c>MainWindow.axaml</c>; Hilfe-Schaltflächen springen darauf.</summary>
public static class TabIndex
{
    public const int Rooms = 0;
    public const int Week = 1;
    public const int House = 2;
    public const int Configuration = 3;
    public const int Devices = 4;
    public const int Cube = 5;
    public const int Status = 6;
    public const int Help = 7;
    public const int Settings = 8;
    public const int About = 9;
}

/// <summary>
/// Gremium Runde 5: Raumkonfiguration, Wandthermostat-Anzeige, Taster, Kopierfunktionen sowie
/// gemeinsame Hilfsmittel (Lokalisierung, Fehlerhinweis, Befehlsaktualisierung).
/// </summary>
public partial class MainWindowViewModel
{
    public Localizer Loc => _loc;

    public bool SupportsAdministration => _service.SupportsAdministration;

    public bool SupportsNetwork => _service.SupportsNetworkConfiguration;

    public IReadOnlyList<int> WindowMinuteOptions { get; } = [0, 5, 10, 15, 20, 25, 30];

    public IReadOnlyList<int> BoostMinuteOptions { get; } = [0, 5, 10, 15, 20, 25, 30, 60];

    public IReadOnlyList<DayOfWeek> DecalcificationDays { get; } = Enum.GetValues<DayOfWeek>();

    public IReadOnlyList<PushButtonKeyMode> PushButtonModes { get; } = Enum.GetValues<PushButtonKeyMode>();

    public ObservableCollection<RoomOption> RoomOptions { get; } = new();

    private string L(string key, params object[] arguments)
        => arguments.Length == 0 ? _loc.Get(key) : _loc.Format(key, arguments);

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private string errorHint = string.Empty;

    public bool HasError => ErrorMessage.Length > 0;

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        ErrorHint = ErrorCatalog.Describe(value, german: _loc.Language == "de") ?? string.Empty;
        ErrorTimeText = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : L("ErrorAt", _clock.Now.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture));
    }

    /// <summary>Schließt den Fehlerdialog (Statusdialog „Fehler“).</summary>
    [RelayCommand]
    private void DismissError() => ErrorMessage = string.Empty;

    // --- Raumkonfiguration (0x11) ----------------------------------------------------

    [ObservableProperty]
    private double configComfort = 21.5;

    [ObservableProperty]
    private double configEco = 16.5;

    [ObservableProperty]
    private double configMaximum = 30.5;

    [ObservableProperty]
    private double configMinimum = 4.5;

    [ObservableProperty]
    private double configWindowTemperature = 12.0;

    [ObservableProperty]
    private int configWindowMinutes = 15;

    [ObservableProperty]
    private double configOffset;

    // --- Boost, Entkalkung, Ventil (0x12) ---------------------------------------------

    [ObservableProperty]
    private int boostMinutes = 15;

    [ObservableProperty]
    private int boostValveAngle = 80;

    [ObservableProperty]
    private DayOfWeek decalcificationDay = DayOfWeek.Saturday;

    [ObservableProperty]
    private int decalcificationHour = 12;

    [ObservableProperty]
    private int valveOffsetPercent;

    [ObservableProperty]
    private bool hasValveConfiguration;

    // --- Taster ---------------------------------------------------------------------

    [ObservableProperty]
    private PushButtonKeyMode pushUpperMode = PushButtonKeyMode.Auto;

    [ObservableProperty]
    private double pushUpperTemperature = 21.0;

    [ObservableProperty]
    private PushButtonKeyMode pushLowerMode = PushButtonKeyMode.Auto;

    [ObservableProperty]
    private double pushLowerTemperature = 17.0;

    // --- Gemeinsame Zustandslogik ----------------------------------------------------

    /// <summary>Lesen bleibt immer möglich; Schreiben setzt eine echte Lesung voraus.</summary>
    private bool CanAdmin => IsConnected && !IsBusy && _service.SupportsAdministration;

    private bool CanWriteAdmin => CanAdmin && !IsStale && CanWriteFrom(ConfigStatus);

    private bool CanSetDisplay
        => CanWriteAdmin && SelectedDevice is { Type: nameof(DeviceType.WallMountedThermostat) };

    private bool CanCopyToRooms
        => CanWriteAdmin && Rooms.Count(room => room.IsHouseSelected && room.Id != SelectedRoom?.Id) > 0;


    private void InitializeAdminState()
    {
        BuildHelpTopics();
        _loadingSettings = true;
        try
        {
            SelectedLanguage = Localizer.Languages.First(language => language.Code == _loc.Language);
        }
        finally
        {
            _loadingSettings = false;
        }

        SelectedZonePreset = TimeZonePresets.All.Keys.First();
    }

    private void LoadAdminStateFor(Cube cube, RoomViewModel? roomViewModel, bool keepEdited)
    {
        ConfigStatus.Update(DisplayState, DisplayTime);
        FirmwareText = FirmwareDescription(cube.FirmwareVersion);
        var room = roomViewModel is null ? null : cube.GetRoom(roomViewModel.Id);
        if (room is null)
        {
            HasValveConfiguration = false;
            RefreshAdminCommands();
            return;
        }

        HasValveConfiguration = room.HasValveConfiguration;
        if (!keepEdited)
        {
            ConfigComfort = room.ComfortTemperature.Value;
            ConfigEco = room.EcoTemperature.Value;
            ConfigMaximum = room.MaximumTemperature.Value;
            ConfigMinimum = room.MinimumTemperature.Value;
            ConfigWindowTemperature = room.WindowOpenTemperature.Value;
            ConfigWindowMinutes = room.WindowOpenDurationMinutes;
            ConfigOffset = room.TemperatureOffset;
            BoostMinutes = room.BoostDurationMinutes;
            BoostValveAngle = room.BoostValveAngle;
            DecalcificationDay = room.DecalcificationDay;
            DecalcificationHour = room.DecalcificationHour;
            ValveOffsetPercent = CubeApplicationService.ValveOffsetToPercent(room.ValveOffsetRaw);
            RenameRoomText = room.Name;
        }

        RefreshAdminCommands();
    }

    private void RebuildRoomOptions(Cube cube)
    {
        var previousMove = SelectedMoveTarget?.Id;
        var previousAssign = SelectedAssignTarget?.Id;
        RoomOptions.Clear();
        RoomOptions.Add(new RoomOption(House.Id, L("HouseNoRoom")));
        foreach (var room in cube.Rooms)
        {
            RoomOptions.Add(new RoomOption(room.Id, room.Name));
        }

        SelectedMoveTarget = RoomOptions.FirstOrDefault(option => option.Id == previousMove) ?? RoomOptions.FirstOrDefault();
        SelectedAssignTarget = RoomOptions.FirstOrDefault(option => option.Id == previousAssign)
            ?? RoomOptions.Skip(1).FirstOrDefault() ?? RoomOptions.FirstOrDefault();
    }

    partial void OnIsConnectedChanged(bool value) => RefreshAdminCommands();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshAdminCommands();
        RefreshStatusBar();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        RenameDeviceText = value?.Name ?? string.Empty;
        RefreshAdminCommands();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(Localizer.Language))
        {
            return;
        }

        OnUiThread(() =>
        {
            WindowTitle = $"{L("AppTitle")} {AppVersion}";
            AppVersionText = L("AppVersion", AppVersion);
            OnPropertyChanged(nameof(AboutVersionText));
            OnPropertyChanged(nameof(AboutAuthorText));
            OnPropertyChanged(nameof(AboutLicenseTextLine));
            OnPropertyChanged(nameof(AboutProjectText));
            OnPropertyChanged(nameof(AboutDisclaimerText));
            OnPropertyChanged(nameof(AboutPrivacyText));
            BuildHelpTopics();
            if (_service.Current is { } cube)
            {
                UpdateRooms(cube, RoomsStatus.State is DataReadState.Cached or DataReadState.Stale
                    ? RoomsStatus.State
                    : null);
            }

            ErrorHint = ErrorCatalog.Describe(ErrorMessage, german: _loc.Language == "de") ?? string.Empty;
            RefreshStatusBar();
            LoadLog();
        });
    }

    private void RefreshAdminCommands()
    {
        foreach (var command in AdminCommands())
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private IEnumerable<IRelayCommand> AdminCommands()
    {
        yield return ApplyRoomConfigurationCommand;
        yield return ApplyBoostAndDecalcificationCommand;
        yield return ApplyValveOffsetCommand;
        yield return CopyConfigurationToSelectedRoomsCommand;
        yield return SetDisplayActualCommand;
        yield return SetDisplaySetpointCommand;
        yield return ReadPushButtonCommand;
        yield return SendPushButtonCommand;
        yield return CopyWeekToSelectedRoomsCommand;
        yield return RenameRoomCommand;
        yield return DeleteRoomCommand;
        yield return MoveRoomUpCommand;
        yield return MoveRoomDownCommand;
        yield return RenameDeviceCommand;
        yield return DeleteDeviceCommand;
        yield return MoveDeviceCommand;
        yield return SearchNewDeviceCommand;
        yield return AssignFoundDeviceCommand;
        yield return ReloadStateCommand;
        yield return ReadTimeZoneCommand;
        yield return SendTimeZoneCommand;
        yield return SetCubeTimeCommand;
        yield return ReadNtpCommand;
        yield return SendNtpCommand;
        yield return ReadNetworkCommand;
        yield return SendNetworkCommand;
        yield return ExportDiagnosticsCommand;
    }

    private void OnAirtimeChanged(object? sender, AirtimeStatus status) => OnUiThread(() =>
    {
        AirtimeText = L("AirtimeInfo", status.DutyCycle, status.FreeMemorySlots);
        CubeInfoStatus.Update(DisplayState, DisplayTime);
    });

    // --- Befehle: Konfiguration ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyRoomConfigurationAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetRoomConfigurationAsync(room.Id, CurrentConfigurationValues(), token);
            await RefreshAuditAsync();
        }, L("ErrConfig"), L("OkConfig"));
    }

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyBoostAndDecalcificationAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetBoostAndDecalcificationAsync(
                room.Id, BoostMinutes, BoostValveAngle, DecalcificationDay, DecalcificationHour, token);
            await RefreshAuditAsync();
        }, L("ErrBoost"), L("OkBoost"));
    }

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyValveOffsetAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetValveOffsetAsync(room.Id, ValveOffsetPercent, token);
            await RefreshAuditAsync();
        }, L("ErrValveOffset"), L("OkValveOffset"));
    }

    /// <summary>
    /// Überträgt die Temperaturkonfiguration auf die in der Haus-Ansicht gewählten Räume
    /// (Kopierfunktion von temperatureConfController.js/boostConfController.js:70-75).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyToRooms))]
    private async Task CopyConfigurationToSelectedRoomsAsync()
    {
        var targets = Rooms.Where(room => room.IsHouseSelected && room.Id != SelectedRoom?.Id).ToArray();
        if (targets.Length == 0)
        {
            SuccessText = L("NoRoomsChosen");
            return;
        }

        await RunAsync(async token =>
        {
            var values = CurrentConfigurationValues();
            foreach (var target in targets)
            {
                await _service.SetRoomConfigurationAsync(target.Id, values, token);
            }

            await RefreshAuditAsync();
        }, L("ErrConfig"), L("OkCopyConfig", targets.Length));
    }

    private RoomConfigurationValues CurrentConfigurationValues() => new(
        new Temperature(ConfigComfort),
        new Temperature(ConfigEco),
        new Temperature(ConfigMaximum),
        new Temperature(ConfigMinimum),
        ConfigOffset,
        new Temperature(ConfigWindowTemperature),
        ConfigWindowMinutes);

    // --- Befehle: Wandthermostat-Anzeige und Taster -----------------------------------

    [RelayCommand(CanExecute = nameof(CanSetDisplay))]
    private Task SetDisplayActualAsync() => SetDisplayAsync(showActual: true);

    [RelayCommand(CanExecute = nameof(CanSetDisplay))]
    private Task SetDisplaySetpointAsync() => SetDisplayAsync(showActual: false);

    private async Task SetDisplayAsync(bool showActual)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetWallThermostatDisplayAsync(RadioAddress.Parse(device.Address), showActual, token);
            await RefreshAuditAsync();
        }, L("ErrDisplay"), L("OkDisplay"));
    }

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task ReadPushButtonAsync()
    {
        await RunAsync(async token =>
        {
            var configuration = await _service.ReadPushButtonAsync(token);
            PushUpperMode = configuration.Upper.Mode;
            PushUpperTemperature = configuration.Upper.Temperature?.Value ?? PushUpperTemperature;
            PushLowerMode = configuration.Lower.Mode;
            PushLowerTemperature = configuration.Lower.Temperature?.Value ?? PushLowerTemperature;
            ConfigStatus.Update(DisplayState, DisplayTime);
        }, L("ErrPushButton"), L("OkPushButtonRead"), status: ConfigStatus);
    }

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task SendPushButtonAsync()
    {
        await RunAsync(async token =>
        {
            var configuration = new PushButtonConfiguration(
                Key(PushUpperMode, PushUpperTemperature), Key(PushLowerMode, PushLowerTemperature));
            await _service.SetPushButtonAsync(configuration, token);
            await RefreshAuditAsync();
        }, L("ErrPushButton"), L("OkPushButton"));
    }

    private static PushButtonKeyConfiguration Key(PushButtonKeyMode mode, double temperature)
        => new(mode, mode is PushButtonKeyMode.AutoWithTemperature or PushButtonKeyMode.Manual
            ? new Temperature(temperature)
            : null);

    // --- Befehle: Wochenprogramm auf andere Räume kopieren ----------------------------

    /// <summary>
    /// Überträgt das Wochenprogramm des gewählten Raums tageweise auf die in der Haus-Ansicht
    /// gewählten Räume. Es werden nur Tage gesendet, die in Quelle UND Ziel vom Cube gelesen
    /// wurden; die Sendesperre (M-F) bleibt dadurch auch hier bestehen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyToRooms))]
    private async Task CopyWeekToSelectedRoomsAsync()
    {
        var source = SelectedRoom;
        if (source is null)
        {
            return;
        }

        var targets = Rooms.Where(room => room.IsHouseSelected && room.Id != source.Id).ToArray();
        var plan = targets
            .SelectMany(target => Enum.GetValues<DayOfWeek>()
                .Where(day => _service.HasWeekProfile(source.Id, day) && _service.HasWeekProfile(target.Id, day))
                .Select(day => (Target: target, Day: day)))
            .ToArray();
        if (plan.Length == 0)
        {
            SuccessText = L("NoLoadedProfiles");
            return;
        }

        await RunAsync(async token =>
        {
            foreach (var (target, day) in plan)
            {
                var profile = _service.GetWeekDayProfile(source.Id, day)!;
                await _service.SetWeekDayProfileAsync(target.Id, profile, token);
            }

            await RefreshAuditAsync();
        }, L("ErrCopyProfile"), L("OkCopyWeek", plan.Length, targets.Length));
    }
}
