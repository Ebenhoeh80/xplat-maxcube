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

    /// <summary>
    /// XAML-Bindungsquelle für lokalisierte Texte. Avalonia wertet einen Indexerpfad nicht
    /// erneut aus, wenn nur <c>Item[]</c> auf dem Indexer gemeldet wird; ein neues Objekt
    /// mit <c>PropertyChanged(nameof(Labels))</c> aktualisiert dagegen alle Bindings.
    /// </summary>
    public Localizer Labels => _labels;

    public bool SupportsAdministration => _service.SupportsAdministration;

    public bool SupportsNetwork => _service.SupportsNetworkConfiguration;

    public IReadOnlyList<int> WindowMinuteOptions { get; } = [0, 5, 10, 15, 20, 25, 30];

    public IReadOnlyList<int> BoostMinuteOptions { get; } = [0, 5, 10, 15, 20, 25, 30, 60];

    public IReadOnlyList<EnumOption<DayOfWeek>> DecalcificationDays
        => Enum.GetValues<DayOfWeek>()
            .Select(day => new EnumOption<DayOfWeek>(day, _loc.Get(EnumLabels.Key(day))))
            .ToArray();

    public IReadOnlyList<EnumOption<PushButtonKeyMode>> PushButtonModes
        => Enum.GetValues<PushButtonKeyMode>()
            .Select(mode => new EnumOption<PushButtonKeyMode>(mode, _loc.Get(EnumLabels.Key(mode))))
            .ToArray();

    /// <summary>Auswahlobjekte für die ComboBox; der Domänenwert bleibt in der VM.</summary>
    public EnumOption<DayOfWeek> SelectedDecalcificationDayOption
    {
        get => DecalcificationDays.First(option => option.Value == DecalcificationDay);
        set => DecalcificationDay = value.Value;
    }

    public EnumOption<PushButtonKeyMode> SelectedPushUpperModeOption
    {
        get => PushButtonModes.First(option => option.Value == PushUpperMode);
        set => PushUpperMode = value.Value;
    }

    public EnumOption<PushButtonKeyMode> SelectedPushLowerModeOption
    {
        get => PushButtonModes.First(option => option.Value == PushLowerMode);
        set => PushLowerMode = value.Value;
    }

    partial void OnDecalcificationDayChanged(DayOfWeek value)
        => OnPropertyChanged(nameof(SelectedDecalcificationDayOption));

    partial void OnPushUpperModeChanged(PushButtonKeyMode value)
        => OnPropertyChanged(nameof(SelectedPushUpperModeOption));

    partial void OnPushLowerModeChanged(PushButtonKeyMode value)
        => OnPropertyChanged(nameof(SelectedPushLowerModeOption));

    public ObservableCollection<RoomOption> RoomOptions { get; } = new();

    private LocalizedValue L(string key, params object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return new LocalizedValue(loc => loc.Get(key), _loc);
        }

        // Ein LocalizedValue als Format-Argument würde von string.Format über ToString()
        // eingefroren. Beim Re-Render muss es in der jetzt gewählten Sprache neu
        // ausgewertet werden, sonst bleibt z. B. der Fehlerprefix deutsch (0.6.5).
        var snapshot = arguments.ToArray();
        return new LocalizedValue(
            loc => loc.Format(key, snapshot.Select(argument =>
                argument is LocalizedValue localized ? localized.In(loc).Current : argument).ToArray()),
            _loc);
    }

    private LocalizedValue L(Func<Localizer, string> render) => new(render, _loc);


    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private string errorHint = string.Empty;

    public bool HasError => ErrorMessage.Length > 0;

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        ErrorHint = BuildErrorHint(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            _localizedBindings.Remove("ErrorTimeText");
            ErrorTimeText = string.Empty;
        }
        else
        {
            var time = _clock.Now.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
            SetLocalized("ErrorTimeText", L("ErrorAt", time), text => ErrorTimeText = text);
        }
    }

    /// <summary>
    /// DE/EN behalten die ausformulierten Katalogtexte; FR/IT/NL/PL erhalten einen
    /// lokalisierten Hinweis mit Code, statt unübersetzt zu bleiben (0.6.3/0.6.4).
    /// </summary>
    private string BuildErrorHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (ErrorCatalog.IsCommandDiscarded(value))
        {
            return L("ErrorCommandDiscarded");
        }

        var code = ErrorCatalog.FindFirstCode(value);
        if (code is null)
        {
            return string.Empty;
        }

        return _loc.Language is "de" or "en"
            ? ErrorCatalog.Describe(value, german: _loc.Language == "de") ?? string.Empty
            : L("ErrorCatalogHint", code);
    }

    /// <summary>Schließt den Fehlerdialog (Statusdialog „Fehler“).</summary>
    [RelayCommand]
    private void DismissError() => SetError(null);

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

    // --- Wartung: Neustart und Werksreset --------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebootCubeCommand))]
    private bool confirmReboot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FactoryResetCubeCommand))]
    private bool confirmFactoryReset;

    private bool CanMaintenance
        => IsConnected && !IsBusy && !IsStale && _service.SupportsMaintenance;

    private bool CanRebootCube => CanMaintenance && ConfirmReboot;

    private bool CanFactoryResetCube => CanMaintenance && ConfirmFactoryReset;

    [RelayCommand(CanExecute = nameof(CanRebootCube))]
    private Task RebootCubeAsync()
        => RunAsync(async token =>
        {
            await _service.RebootCubeAsync(true, token);
            OnUiThread(() => ConfirmReboot = false);
        }, L("ErrReboot"), L("OkReboot"), previewText: L("PreviewReboot"));

    [RelayCommand(CanExecute = nameof(CanFactoryResetCube))]
    private Task FactoryResetCubeAsync()
        => RunAsync(async token =>
        {
            await _service.FactoryResetCubeAsync(true, token);
            OnUiThread(() => ConfirmFactoryReset = false);
        }, L("ErrFactoryReset"), L("OkFactoryReset"), previewText: L("PreviewFactoryReset"));

    // --- Wake-up (v0.8.0): explizit bestätigt, nie automatisch vor anderen Schreiben ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WakeUpRoomCommand))]
    private bool confirmWakeUp;

    [ObservableProperty]
    private double wakeUpMinutes = 30;

    private bool CanWakeUpRoom => CanWriteAdmin && ConfirmWakeUp && SelectedConfigRoom is not null;

    [RelayCommand(CanExecute = nameof(CanWakeUpRoom))]
    private Task WakeUpRoomAsync()
    {
        var name = SelectedConfigRoom!.Name;
        var roomId = SelectedConfigRoom.Id;
        var minutes = (int)Math.Clamp(WakeUpMinutes, 0, 0xFF);
        return RunAsync(async token =>
        {
            await _service.WakeUpRoomAsync(roomId, true, minutes, token);
            OnUiThread(() => ConfirmWakeUp = false);
        }, L("ErrWakeUp"), L("OkWakeUp"),
           previewText: L(localizer => localizer.Format("PreviewWakeUp", name, minutes)));
    }

    // --- Gemeinsame Zustandslogik ----------------------------------------------------

    /// <summary>Lesen bleibt immer möglich; Schreiben setzt eine echte Lesung voraus.</summary>
    private bool CanAdmin => IsConnected && !IsBusy && _service.SupportsAdministration;

    private bool CanWriteAdmin => CanAdmin && !IsStale && CanWriteFrom(ConfigStatus);

    private bool CanSetDisplay
        => CanWriteAdmin && SelectedDevice?.DeviceType == DeviceType.WallMountedThermostat;

    private bool CanCopyToRooms(RoomViewModel? source)
        => CanWriteAdmin && Rooms.Count(room => room.IsHouseSelected && room.Id != source?.Id) > 0;

    private bool CanCopyConfigurationToRooms => CanCopyToRooms(SelectedConfigRoom);

    private bool CanCopyWeekToRooms => CanCopyToRooms(SelectedWeekRoom);


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
            MarkConfigurationSaved();
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
        }

        // Nur die tatsächlich übernommenen Cube-Werte sind die Basis. Bei keepEdited
        // bleiben getippte Werte stehen und dürfen nicht als gespeichert markiert werden.
        if (!keepEdited)
        {
            MarkConfigurationSaved();
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
            // Ein neues Labels-Objekt zwingt Avalonia zur Neubewertung aller
            // {Binding Labels[Schlüssel]}-Pfade. Item[] allein genügt nicht (Headless-Test).
            _labels = new Localizer(_loc.Language);
            OnPropertyChanged(nameof(Labels));
            WindowTitle = $"{L("AppTitle").Current} {AppVersion}";
            AppVersionText = L("AppVersion", AppVersion);
            OnPropertyChanged(nameof(AboutVersionText));
            OnPropertyChanged(nameof(AboutAuthorText));
            OnPropertyChanged(nameof(AboutLicenseTextLine));
            OnPropertyChanged(nameof(AboutProjectText));
            OnPropertyChanged(nameof(AboutDisclaimerText));
            OnPropertyChanged(nameof(AboutPrivacyText));
            OnPropertyChanged(nameof(DecalcificationDays));
            OnPropertyChanged(nameof(SelectedDecalcificationDayOption));
            OnPropertyChanged(nameof(PushButtonModes));
            OnPropertyChanged(nameof(SelectedPushUpperModeOption));
            OnPropertyChanged(nameof(SelectedPushLowerModeOption));
            OnPropertyChanged(nameof(ZonePresets));
            OnPropertyChanged(nameof(SelectedZonePresetOption));
            BuildHelpTopics();
            if (_service.Current is { } cube)
            {
                UpdateRooms(cube, RoomsStatus.State is DataReadState.Cached or DataReadState.Stale
                    ? RoomsStatus.State
                    : null);
            }

            // Gespeicherte Texte (Status, Erfolg, Fehler, Vorschau, Dirty …) werden in der
            // neuen Sprache neu erzeugt; der Cube wird dabei nicht angesprochen.
            RefreshLocalizedBindings();
            RefreshNumericBindings();
            RefreshDirtyState();
            ErrorHint = BuildErrorHint(ErrorMessage);
            RefreshStatusBar();
            LoadLog();
        });
    }

    /// <summary>
    /// Zahlenbindings werden von Avalonia nur bei einer PropertyChanged neu formatiert. Der
    /// Sprachwechsel ändert die Kultur, aber nicht die Werte – daher explizit benachrichtigen.
    /// </summary>
    private void RefreshNumericBindings()
    {
        foreach (var property in new[]
        {
            nameof(RequestedTemperature),
            nameof(ConfigComfort), nameof(ConfigEco), nameof(ConfigMaximum), nameof(ConfigMinimum),
            nameof(ConfigWindowTemperature), nameof(ConfigOffset),
            nameof(BoostMinutes), nameof(BoostValveAngle), nameof(ValveOffsetPercent),
            nameof(PushUpperTemperature), nameof(PushLowerTemperature),
        })
        {
            OnPropertyChanged(property);
        }

        foreach (var point in WeekDays.SelectMany(day => day.Points))
        {
            point.RefreshCulture();
        }
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
        yield return RebootCubeCommand;
        yield return FactoryResetCubeCommand;
        yield return WakeUpRoomCommand;
        yield return ExportDiagnosticsCommand;
    }

    private void OnAirtimeChanged(object? sender, AirtimeStatus status) => OnUiThread(() =>
    {
        SetAirtime(L("AirtimeInfo", status.DutyCycle, status.FreeMemorySlots));
        CubeInfoStatus.Update(DisplayState, DisplayTime);
    });

    // --- Befehle: Konfiguration ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyRoomConfigurationAsync()
    {
        var room = SelectedConfigRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetRoomConfigurationAsync(room.Id, CurrentConfigurationValues(), token);
            await RefreshAuditAsync();
            MarkConfigurationSaved();
        }, L("ErrConfig"), L("OkConfig"),
            previewText: PreviewConfiguration(L("SendConfig")), contextText: L("SendConfig").Append(" …"));
    }

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyBoostAndDecalcificationAsync()
    {
        var room = SelectedConfigRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetBoostAndDecalcificationAsync(
                room.Id, BoostMinutes, BoostValveAngle, DecalcificationDay, DecalcificationHour, token);
            await RefreshAuditAsync();
            MarkConfigurationSaved();
        }, L("ErrBoost"), L("OkBoost"));
    }

    [RelayCommand(CanExecute = nameof(CanWriteAdmin))]
    private async Task ApplyValveOffsetAsync()
    {
        var room = SelectedConfigRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.SetValveOffsetAsync(room.Id, ValveOffsetPercent, token);
            await RefreshAuditAsync();
            MarkConfigurationSaved();
        }, L("ErrValveOffset"), L("OkValveOffset"));
    }

    /// <summary>
    /// Überträgt die Temperaturkonfiguration auf die in der Haus-Ansicht gewählten Räume
    /// (Kopierfunktion von temperatureConfController.js/boostConfController.js:70-75).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyConfigurationToRooms))]
    private async Task CopyConfigurationToSelectedRoomsAsync()
    {
        var targets = Rooms.Where(room => room.IsHouseSelected && room.Id != SelectedConfigRoom?.Id).ToArray();
        if (targets.Length == 0)
        {
            SetSuccess(L("NoRoomsChosen"));
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
        }, L("ErrConfig"), L("OkCopyConfig", targets.Length),
            previewText: PreviewConfigurationCopy(targets.Length),
            contextText: L("CopyConfig").Append(" …"));
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
    [RelayCommand(CanExecute = nameof(CanCopyWeekToRooms))]
    private async Task CopyWeekToSelectedRoomsAsync()
    {
        var source = SelectedWeekRoom;
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
            SetSuccess(L("NoLoadedProfiles"));
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
        }, L("ErrCopyProfile"), L("OkCopyWeek", plan.Length, targets.Length),
            previewText: PreviewWeekCopy(plan.Length), contextText: L("CopyWeek").Append(" …"));
    }
}
