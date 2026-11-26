using System.Text.RegularExpressions;
using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.UI.Localization;
using MaxCube.UI.ViewModels;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.Tests;

/// <summary>Gremium Runde 5: Lokalisierung, Konfiguration, Struktur, Cube-Einstellungen, Hilfe, Einstellungen.</summary>
public class LocalizationTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MaxCube.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("MaxCube.slnx nicht gefunden.");
    }

    [Fact]
    public void Every_key_has_six_non_empty_texts_with_identical_placeholders()
    {
        Localizer.Languages.Select(language => language.Code).Should().Equal("de", "en", "fr", "it", "nl", "pl");
        foreach (var key in Localizer.Keys)
        {
            var texts = Localizer.TextsFor(key);
            texts.Should().HaveCount(6, key);
            texts.Should().OnlyContain(text => !string.IsNullOrWhiteSpace(text), $"{key} braucht alle sechs Sprachen");
            var placeholders = texts.Select(text =>
                string.Join(',', Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}").Select(match => match.Groups[1].Value).Distinct().Order()));
            placeholders.Distinct().Should().HaveCount(1, $"{key}: Platzhalter müssen in allen Sprachen gleich sein");
        }
    }

    [Fact]
    public void Every_key_used_in_xaml_or_code_exists_in_the_catalog()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "MaxCube.UI");
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => (file.EndsWith(".cs") || file.EndsWith(".axaml"))
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.EndsWith("Strings.cs"))
            .ToArray();
        var used = new SortedSet<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, @"Loc\[(\w+)\]")) used.Add(match.Groups[1].Value);
            foreach (Match match in Regex.Matches(text, @"\bL\(""(\w+)""")) used.Add(match.Groups[1].Value);
            foreach (Match match in Regex.Matches(text, @"_loc\.(?:Get|Format)\(""(\w+)""")) used.Add(match.Groups[1].Value);
            foreach (Match match in Regex.Matches(text, @"L\(\s*[\w.]+\s*\?\s*""(\w+)""\s*:\s*""(\w+)""")) { used.Add(match.Groups[1].Value); used.Add(match.Groups[2].Value); }
            foreach (Match match in Regex.Matches(text, @"zone\.UsesDaylightSaving\s*\?\s*""(\w+)""\s*:\s*""(\w+)""")) { used.Add(match.Groups[1].Value); used.Add(match.Groups[2].Value); }
        }

        used.Remove("Schlüssel"); // steht nur in einem Kommentar des Lokalisierers
        used.Should().NotBeEmpty();
        used.Should().OnlyContain(key => Localizer.Keys.Contains(key), "jeder verwendete Schlüssel braucht einen Text");
        foreach (var topic in new[] { "Connect", "Rooms", "Week", "Config", "Devices", "Cube" })
        {
            Localizer.Keys.Should().Contain($"Help{topic}Title").And.Contain($"Help{topic}Body");
        }
    }

    [Fact]
    public void German_status_texts_are_the_historic_ones()
    {
        var loc = new Localizer();

        loc["StatusDisconnected"].Should().Be("Getrennt");
        loc.Format("StatusConnected", "JEQ0001234", 275).Should().Be("Verbunden · JEQ0001234 · FW 275");
        loc.Format("OkCopyProfile", 3).Should().Be("Profil auf 3 Tag(e) kopiert.");
        loc["Cancelled"].Should().Be("Abgebrochen.");
    }

    [Fact]
    public void Switching_the_language_notifies_the_indexer_and_unknown_keys_are_visible()
    {
        var loc = new Localizer("de");
        var notifications = new List<string?>();
        loc.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        loc.SetLanguage("fr");
        loc.SetLanguage("fr");
        loc.SetLanguage("xx");

        loc["Connect"].Should().Be("Verbinden");
        notifications.Should().Equal("Item[]", "Language", "Item[]", "Language");
        loc.SetLanguage("pl");
        loc["Connect"].Should().Be("Połącz");
        loc["GibtEsNicht"].Should().Be("!GibtEsNicht!");
    }

    [Theory]
    [InlineData(null, "fr", "fr")]
    [InlineData("it", "fr", "it")]
    [InlineData("IT ", "fr", "it")]
    [InlineData("xx", "nl", "nl")]
    [InlineData(null, "ja", "de")]
    [InlineData(null, null, "de")]
    public void Language_selection_prefers_the_stored_value_then_the_system_then_german(
        string? stored, string? system, string expected)
    {
        Localizer.Detect(stored, system).Should().Be(expected);
    }
}

public class AdminViewModelTests : IDisposable
{
    private readonly string _reports = Path.Combine(Path.GetTempPath(), "maxcube-vm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_reports, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist optional.
        }
    }

    private static readonly RadioAddress Heating = new(0x0A0B0C);
    private static readonly RadioAddress Wall = new(0x0A0B0D);
    private static readonly RadioAddress Kitchen = new(0x0A0B20);

    private static Cube BuildCube()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", Heating));
        cube.AddRoom(new Room(2, "Küche", Kitchen));
        cube.AddDevice(new Device(Heating, DeviceType.HeatingThermostat, 1, new SerialNumber("JEQ0000001"), "Heizung Wohnen", 1));
        cube.AddDevice(new Device(Wall, DeviceType.WallMountedThermostat, 3, new SerialNumber("JEQ0000002"), "Wand Wohnen", 1));
        cube.AddDevice(new Device(Kitchen, DeviceType.HeatingThermostat, 1, new SerialNumber("JEQ0000004"), "Heizung Küche", 2));
        cube.ApplyRoomConfiguration(
            1, new Temperature(22.0), new Temperature(17.0), new Temperature(28.0), new Temperature(6.0),
            0.5, new Temperature(11.0), 10, 30, 60, DayOfWeek.Monday, 7);
        cube.ApplyValveConfiguration(1, 255, 51);
        foreach (var roomId in new[] { 1, 2 })
        {
            var profile = new WeekTemperatureProfile();
            profile.SetDay(new DayProfile(DayOfWeek.Monday,
            [
                new SwitchPoint(new TimeOnly(6, 0), new Temperature(17.0)),
                new SwitchPoint(TimeOnly.MinValue, new Temperature(21.0)),
            ]));
            cube.ApplyWeekProfile(roomId, profile);
        }

        return cube;
    }

    private sealed class Harness
    {
        public required MainWindowViewModel ViewModel { get; init; }
        public required FakeCore Core { get; init; }
        public required FakeAdmin Admin { get; init; }
        public required FakeStore Store { get; init; }
        public required Cube Cube { get; init; }
        public required FakeNetwork Network { get; init; }
    }

    private static async Task<Harness> CreateAsync(bool withAdmin = true, bool connect = true)
    {
        var cube = BuildCube();
        var core = new FakeCore { CubeToReturn = cube };
        var store = new FakeStore();
        var admin = new FakeAdmin();
        var network = new FakeNetwork();
        var clock = new FakeClock();
        var service = new CubeApplicationService(
            core, store, clock, admin: withAdmin ? admin : null, network: withAdmin ? network : null);
        var viewModel = new MainWindowViewModel(service, clock);
        if (connect)
        {
            await viewModel.ConnectCommand.ExecuteAsync(null);
        }

        return new Harness { ViewModel = viewModel, Core = core, Admin = admin, Store = store, Cube = cube, Network = network };
    }

    [Fact]
    public async Task Switching_the_language_updates_titles_texts_and_help()
    {
        var h = await CreateAsync();
        var vm = h.ViewModel;
        vm.WindowTitle.Should().StartWith("MAX! Cube – Lokale App");
        vm.HelpTopics.Should().HaveCount(6);

        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "en");

        vm.WindowTitle.Should().StartWith("MAX! Cube – Local App");
        vm.Loc["Connect"].Should().Be("Connect");
        vm.HelpTopics[0].Title.Should().Be("Connecting and finding the cube");
        vm.Rooms[0].ActualText.Should().Be("Actual: —");
        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.StatusText.Should().Be("Disconnected");
    }

    [Fact]
    public async Task Connecting_loads_the_room_configuration_into_the_editor_fields()
    {
        var vm = (await CreateAsync()).ViewModel;

        vm.ConfigComfort.Should().Be(22.0);
        vm.ConfigEco.Should().Be(17.0);
        vm.ConfigMaximum.Should().Be(28.0);
        vm.ConfigMinimum.Should().Be(6.0);
        vm.ConfigWindowTemperature.Should().Be(11.0);
        vm.ConfigWindowMinutes.Should().Be(10);
        vm.ConfigOffset.Should().Be(0.5);
        vm.BoostMinutes.Should().Be(30);
        vm.BoostValveAngle.Should().Be(60);
        vm.DecalcificationDay.Should().Be(DayOfWeek.Monday);
        vm.DecalcificationHour.Should().Be(7);
        vm.ValveOffsetPercent.Should().Be(20);
        vm.HasValveConfiguration.Should().BeTrue();
        vm.RenameRoomText.Should().Be("Wohnen");
        vm.FirmwareText.Should().Contain("unterstützt");
    }

    [Fact]
    public async Task Configuration_commands_need_a_connection_and_the_admin_gateway()
    {
        var offline = await CreateAsync(connect: false);
        offline.ViewModel.ApplyRoomConfigurationCommand.CanExecute(null).Should().BeFalse();

        var withoutAdmin = await CreateAsync(withAdmin: false);
        withoutAdmin.ViewModel.ApplyRoomConfigurationCommand.CanExecute(null).Should().BeFalse();
        withoutAdmin.ViewModel.SupportsAdministration.Should().BeFalse();

        var online = await CreateAsync();
        online.ViewModel.ApplyRoomConfigurationCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Sending_the_configuration_uses_the_edited_values()
    {
        var h = await CreateAsync();
        h.ViewModel.ConfigComfort = 23.0;
        h.ViewModel.ConfigWindowMinutes = 5;

        await h.ViewModel.ApplyRoomConfigurationCommand.ExecuteAsync(null);

        h.ViewModel.IsWritePreviewVisible.Should().BeTrue();
        h.ViewModel.WritePreviewText.Should().Contain("23.0").And.Contain("Wohnen");
        h.Admin.Configurations.Should().BeEmpty("die Vorschau darf noch nicht senden");

        await h.ViewModel.ConfirmWriteCommand.ExecuteAsync(null);

        var sent = h.Admin.Configurations.Should().ContainSingle().Subject;
        sent.Comfort.Value.Should().Be(23.0);
        sent.WindowOpenMinutes.Should().Be(5);
        h.ViewModel.SuccessText.Should().Be("Konfiguration gesendet.");
        h.Cube.GetRoom(1)!.ComfortTemperature.Value.Should().Be(23.0);
    }

    [Fact]
    public async Task A_rejected_configuration_opens_the_error_dialog_with_the_code_explanation()
    {
        var h = await CreateAsync();
        h.ViewModel.ConfigMinimum = 29.0;
        h.ViewModel.ConfigMaximum = 25.0;

        await h.ViewModel.ApplyRoomConfigurationCommand.ExecuteAsync(null);
        await h.ViewModel.ConfirmWriteCommand.ExecuteAsync(null);

        h.ViewModel.HasError.Should().BeTrue();
        h.ViewModel.ErrorMessage.Should().Contain("MAX1050");
        h.ViewModel.ErrorHint.Should().Be("MAX1050: Eingabe ist ungültig");
        h.Admin.Configurations.Should().BeEmpty();

        h.ViewModel.DismissErrorCommand.Execute(null);
        h.ViewModel.HasError.Should().BeFalse();
        h.ViewModel.ErrorHint.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_preview_sends_nothing_and_keeps_edit_unsaved()
    {
        var h = await CreateAsync();
        h.ViewModel.ConfigComfort = 24.0;

        await h.ViewModel.ApplyRoomConfigurationCommand.ExecuteAsync(null);

        h.ViewModel.IsWritePreviewVisible.Should().BeTrue();
        h.ViewModel.CancelWriteCommand.Execute(null);
        h.ViewModel.IsWritePreviewVisible.Should().BeFalse();
        h.ViewModel.ConfirmWriteCommand.CanExecute(null).Should().BeFalse();
        h.Admin.Configurations.Should().BeEmpty();
        h.ViewModel.DirtyText.Should().Contain("Konfiguration");
    }

    [Fact]
    public async Task Editing_shows_dirty_and_a_successful_save_clears_only_that_area()
    {
        var h = await CreateAsync();
        var vm = h.ViewModel;
        vm.HasDirty.Should().BeFalse();

        vm.SelectedDay.Points[0].Temperature = 18.5;
        vm.ConfigComfort = 23.0;

        vm.DirtyText.Should().Contain("Wochenprofil").And.Contain("Konfiguration");

        await vm.SaveWeekProfileCommand.ExecuteAsync(null);
        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        vm.DirtyText.Should().NotContain("Wochenprofil").And.Contain("Konfiguration");

        await vm.ApplyRoomConfigurationCommand.ExecuteAsync(null);
        await vm.ConfirmWriteCommand.ExecuteAsync(null);

        vm.HasDirty.Should().BeFalse();
        vm.DirtyText.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_write_buttons_show_a_localized_reason()
    {
        var h = await CreateAsync(connect: false);
        h.ViewModel.WeekWriteHint.Should().Contain("keine Verbindung");
        h.ViewModel.ConfigWriteHint.Should().Contain("keine Verbindung");
        h.ViewModel.HouseWriteHint.Should().Be("Keine Räume ausgewählt.");

        await h.ViewModel.ConnectCommand.ExecuteAsync(null);

        h.ViewModel.WeekWriteHint.Should().Be("Bereit zum Senden");
        h.ViewModel.SelectedDay = h.ViewModel.WeekDays.First(day => day.Day == DayOfWeek.Tuesday);
        h.ViewModel.WeekWriteHint.Should().Contain("Wochenprofil nicht gelesen");
        h.ViewModel.ConfigWriteHint.Should().Be("Bereit zum Senden");

        var withoutAdmin = await CreateAsync(withAdmin: false);
        withoutAdmin.ViewModel.ConfigWriteHint.Should().Contain("unterstützt keine Konfiguration");
    }

    [Fact]
    public async Task Boost_valve_offset_and_display_commands_reach_the_cube()
    {
        var h = await CreateAsync();
        h.ViewModel.BoostMinutes = 15;
        await h.ViewModel.ApplyBoostAndDecalcificationCommand.ExecuteAsync(null);
        h.ViewModel.ValveOffsetPercent = 40;
        await h.ViewModel.ApplyValveOffsetCommand.ExecuteAsync(null);

        h.ViewModel.SetDisplayActualCommand.CanExecute(null).Should().BeFalse("kein Wandthermostat ausgewählt");
        h.ViewModel.SelectedDevice = h.ViewModel.Devices.Single(device => device.Type == "WallMountedThermostat");
        h.ViewModel.SetDisplayActualCommand.CanExecute(null).Should().BeTrue();
        await h.ViewModel.SetDisplayActualCommand.ExecuteAsync(null);

        h.Admin.ValveFunctions.Should().HaveCount(2);
        h.Admin.ValveFunctions[1].ValveOffsetRaw.Should().Be(102);
        h.Admin.Displays.Should().ContainSingle().Which.Should().Be((Wall, true));
    }

    [Fact]
    public async Task Bundling_queues_the_temperature_instead_of_sending_it()
    {
        var h = await CreateAsync();
        h.ViewModel.BundleTemperatures = true;
        h.ViewModel.RequestedTemperature = 19.5;

        await h.ViewModel.ApplyTemperatureCommand.ExecuteAsync(null);

        h.Core.TemperatureCalls.Should().BeEmpty();
        h.ViewModel.SuccessText.Should().Contain("vorgemerkt").And.Contain("3 s");
        h.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Structure_changes_need_the_confirmation_checkbox()
    {
        var h = await CreateAsync();
        h.ViewModel.SelectedDevice = h.ViewModel.Devices.Single(device => device.Type == "WallMountedThermostat");

        h.ViewModel.DeleteDeviceCommand.CanExecute(null).Should().BeFalse();
        h.ViewModel.ConfirmChanges = true;
        h.ViewModel.DeleteDeviceCommand.CanExecute(null).Should().BeTrue();

        await h.ViewModel.DeleteDeviceCommand.ExecuteAsync(null);

        h.Admin.Calls.Should().ContainInOrder($"delete:{Wall}:soft", "metadata");
        h.ViewModel.ConfirmChanges.Should().BeFalse("die Bestätigung gilt nur für eine Änderung");
        h.Cube.GetDevice(Wall).Should().BeNull();
        h.ViewModel.Devices.Should().NotContain(device => device.Address == Wall.ToString());
    }

    [Fact]
    public async Task Renaming_and_reordering_rooms_work_from_the_viewmodel()
    {
        var h = await CreateAsync();
        h.ViewModel.RenameRoomText = "Wohnzimmer";

        await h.ViewModel.RenameRoomCommand.ExecuteAsync(null);
        await h.ViewModel.MoveRoomDownCommand.ExecuteAsync(null);

        h.Cube.GetRoom(1)!.Name.Should().Be("Wohnzimmer");
        h.Cube.Rooms.Select(room => room.Id).Should().Equal(2, 1);
        h.Admin.Metadata.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_search_offers_the_device_and_the_assignment_needs_name_and_confirmation()
    {
        var h = await CreateAsync();
        h.Admin.Found.Enqueue(new FoundDevice(DeviceType.ShutterContact, 4, new RadioAddress(0x0A0B0E), "JEQ0000009"));

        await h.ViewModel.SearchNewDeviceCommand.ExecuteAsync(null);

        h.ViewModel.FoundDevice.Should().NotBeNull();
        h.ViewModel.FoundDeviceText.Should().Contain("0A0B0E").And.Contain("JEQ0000009");
        h.ViewModel.AssignDeviceName.Should().Be("ShutterContact JEQ0000009");
        h.ViewModel.AssignFoundDeviceCommand.CanExecute(null).Should().BeFalse("ohne Bestätigung");

        h.ViewModel.SelectedAssignTarget = h.ViewModel.RoomOptions.Single(option => option.Id == 1);
        h.ViewModel.AssignDeviceName = "Fenster Wohnen";
        h.ViewModel.ConfirmChanges = true;
        await h.ViewModel.AssignFoundDeviceCommand.ExecuteAsync(null);

        h.Cube.GetDevice(new RadioAddress(0x0A0B0E))!.RoomId.Should().Be(1);
        h.ViewModel.FoundDevice.Should().BeNull();
        h.ViewModel.ConfirmChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Cube_settings_read_and_write_time_zone_ntp_and_push_button()
    {
        var h = await CreateAsync();

        await h.ViewModel.ReadTimeZoneCommand.ExecuteAsync(null);
        h.ViewModel.TimeZoneText.Should().Contain("CET").And.Contain("CEST").And.Contain("UTC+1");
        h.ViewModel.SelectedZonePreset.Should().Be("Mitteleuropa (CET/CEST)");

        h.ViewModel.SelectedZonePreset = "Westeuropa (GMT/BST)";
        await h.ViewModel.SendTimeZoneCommand.ExecuteAsync(null);
        await h.ViewModel.SetCubeTimeCommand.ExecuteAsync(null);
        h.Admin.TimeZones.Should().HaveCount(2);
        h.Admin.TimeZones[0].Configuration.Winter.Name.Should().Be("GMT");

        h.ViewModel.NtpServer1 = "pool.ntp.org";
        h.ViewModel.NtpServer2 = "time.eq-3.de";
        await h.ViewModel.SendNtpCommand.ExecuteAsync(null);
        h.Admin.Ntp.Should().ContainSingle();

        await h.ViewModel.ReadPushButtonCommand.ExecuteAsync(null);
        h.ViewModel.PushUpperMode.Should().Be(PushButtonKeyMode.Comfort);
        h.ViewModel.PushUpperMode = PushButtonKeyMode.Manual;
        h.ViewModel.PushUpperTemperature = 22.5;
        await h.ViewModel.SendPushButtonCommand.ExecuteAsync(null);
        h.Admin.PushButtons.Should().ContainSingle().Which.Upper.Temperature!.Value.Value.Should().Be(22.5);
    }

    [Fact]
    public async Task The_network_needs_a_confirmation_and_valid_addresses()
    {
        var h = await CreateAsync();
        await h.ViewModel.ReadNetworkCommand.ExecuteAsync(null);
        h.ViewModel.NetworkIp.Should().Be("192.168.178.3");
        h.ViewModel.NetworkDhcp.Should().BeTrue();

        h.ViewModel.SendNetworkCommand.CanExecute(null).Should().BeFalse();
        h.ViewModel.ConfirmNetwork = true;
        h.ViewModel.SendNetworkCommand.CanExecute(null).Should().BeTrue();
        h.ViewModel.NetworkGateway = "1.2.3";
        await h.ViewModel.SendNetworkCommand.ExecuteAsync(null);
        h.ViewModel.HasError.Should().BeTrue();
        h.Network.Written.Should().BeEmpty();

        h.ViewModel.DismissErrorCommand.Execute(null);
        h.ViewModel.NetworkGateway = "192.168.178.1";
        await h.ViewModel.SendNetworkCommand.ExecuteAsync(null);
        h.Network.Written.Should().ContainSingle();
        h.ViewModel.ConfirmNetwork.Should().BeFalse();
    }

    [Fact]
    public async Task The_diagnostic_report_is_shown_and_saved_locally()
    {
        var h = await CreateAsync();
        h.ViewModel.ReportDirectory = _reports;

        await h.ViewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        var file = Directory.GetFiles(_reports, "maxcube-diagnose-*.txt").Should().ContainSingle().Subject;
        File.ReadAllText(file).Should().Contain("JEQ0001234").And.Contain("Firmware 275");
        h.ViewModel.DiagnosticText.Should().Contain("Gespeichert unter").And.Contain("Diagnosebericht");
    }

    [Fact]
    public async Task Airtime_updates_after_a_radiogram()
    {
        var h = await CreateAsync();

        h.Admin.RaiseAirtime(new AirtimeStatus(9, 47));

        h.ViewModel.AirtimeText.Should().Be("Funkauslastung: 9, freie Speicherplätze: 47");
    }

    [Fact]
    public async Task Context_help_jumps_to_the_help_tab_and_selects_the_topic()
    {
        var vm = (await CreateAsync()).ViewModel;

        vm.ShowHelpCommand.Execute("Config");

        vm.SelectedTabIndex.Should().Be(TabIndex.Help);
        vm.SelectedHelpTopic!.Key.Should().Be("Config");
        vm.SelectedHelpTopic.Body.Should().Contain("Comfort");
    }

    [Fact]
    public async Task Settings_are_loaded_applied_and_persisted()
    {
        var h = await CreateAsync(connect: false);
        h.Store.Settings[MainWindowViewModel.LanguageSetting] = "fr";
        h.Store.Settings[MainWindowViewModel.LogRetentionSetting] = "14";
        h.Store.Settings[MainWindowViewModel.BundleSetting] = "1";
        h.Store.Settings[MainWindowViewModel.CloseToTraySetting] = "1";
        var vm = h.ViewModel;

        await vm.InitializeAsync("it");

        vm.Loc.Language.Should().Be("fr");
        vm.LogRetentionDays.Should().Be(14);
        vm.BundleTemperatures.Should().BeTrue();
        vm.CloseToTray.Should().BeTrue();

        vm.SelectedLanguage = vm.Languages.Single(language => language.Code == "nl");
        vm.LogRetentionDays = 7;
        await Task.Delay(100);
        h.Store.Settings[MainWindowViewModel.LanguageSetting].Should().Be("nl");
        h.Store.Settings[MainWindowViewModel.LogRetentionSetting].Should().Be("7");
    }

    [Fact]
    public async Task Without_stored_settings_the_system_language_and_defaults_apply()
    {
        var h = await CreateAsync(connect: false);

        await h.ViewModel.InitializeAsync("pl");

        h.ViewModel.Loc.Language.Should().Be("pl");
        h.ViewModel.LogRetentionDays.Should().Be(MainWindowViewModel.DefaultLogRetentionDays);
        h.ViewModel.BundleTemperatures.Should().BeFalse();
        h.Store.Settings.Should().NotContainKey(MainWindowViewModel.LanguageSetting, "die Erkennung wird nicht als Nutzerwahl gespeichert");
    }

    [Fact]
    public async Task The_week_can_be_copied_to_the_selected_rooms_for_loaded_days_only()
    {
        var h = await CreateAsync();
        h.ViewModel.Rooms.Single(room => room.Id == 2).IsHouseSelected = true;
        h.ViewModel.CopyWeekToSelectedRoomsCommand.NotifyCanExecuteChanged();

        await h.ViewModel.CopyWeekToSelectedRoomsCommand.ExecuteAsync(null);

        h.ViewModel.IsWritePreviewVisible.Should().BeTrue();
        h.Core.ProfileCalls.Should().BeEmpty();

        await h.ViewModel.ConfirmWriteCommand.ExecuteAsync(null);

        h.Core.ProfileCalls.Should().ContainSingle().Which.Should().Be((2, DayOfWeek.Monday));
        h.ViewModel.SuccessText.Should().Be("1 Tagesprofile auf 1 Raum/Räume kopiert.");
    }

    [Fact]
    public async Task The_configuration_can_be_copied_to_the_selected_rooms()
    {
        var h = await CreateAsync();
        h.ViewModel.Rooms.Single(room => room.Id == 2).IsHouseSelected = true;
        h.ViewModel.CopyConfigurationToSelectedRoomsCommand.NotifyCanExecuteChanged();

        await h.ViewModel.CopyConfigurationToSelectedRoomsCommand.ExecuteAsync(null);

        h.ViewModel.IsWritePreviewVisible.Should().BeTrue();
        h.Admin.Calls.Should().NotContain("config:2");

        await h.ViewModel.ConfirmWriteCommand.ExecuteAsync(null);

        h.Admin.Calls.Should().Contain("config:2");
        h.Cube.GetRoom(2)!.ComfortTemperature.Value.Should().Be(22.0);
    }

    // --- Tagesleiste und Schnellbearbeitung (ohne Verbindung) --------------------------

    [Fact]
    public void The_day_bar_follows_the_switch_points()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        day.Points.Add(new SwitchPointViewModel { UntilText = "06:00", Temperature = 17.0 });
        day.Points.Add(new SwitchPointViewModel { UntilText = "22:00", Temperature = 21.5 });
        day.Points.Add(new SwitchPointViewModel { UntilText = "00:00", Temperature = 17.0 });

        day.Segments.Should().HaveCount(3);
        day.Segments[0].Start.Should().Be(0);
        day.Segments[0].Width.Should().BeApproximately(0.25, 1e-9);
        day.Segments[1].Width.Should().BeApproximately(16 / 24.0, 1e-9);
        day.Segments.Sum(segment => segment.Width).Should().BeApproximately(1.0, 1e-9);

        day.Points[1].UntilText = "20:00";
        day.Segments[2].Width.Should().BeApproximately(4 / 24.0, 1e-9, "die Leiste aktualisiert sich bei Änderungen");
        day.Points[0].UntilText = "kaputt";
        day.Segments.Should().HaveCount(2, "ungültige Zeiten werden übersprungen statt die Leiste zu brechen");
    }

    [Fact]
    public void Colours_run_from_blue_over_yellow_to_red()
    {
        DayProfileViewModel.ColorFor(4.5).Should().Be("#3A7BD5");
        DayProfileViewModel.ColorFor(17.5).Should().Be("#F2C14E");
        DayProfileViewModel.ColorFor(30.5).Should().Be("#D84B3A");
        DayProfileViewModel.ColorFor(-10).Should().Be("#3A7BD5");
        DayProfileViewModel.ColorFor(99).Should().Be("#D84B3A");
    }

    [Fact]
    public void Quick_edit_moves_time_and_temperature_within_the_valid_range()
    {
        var point = new SwitchPointViewModel { UntilText = "06:00", Temperature = 21.0 };

        point.LaterCommand.Execute(null);
        point.UntilText.Should().Be("06:05");
        point.EarlierCommand.Execute(null);
        point.EarlierCommand.Execute(null);
        point.UntilText.Should().Be("05:55");
        point.WarmerCommand.Execute(null);
        point.Temperature.Should().Be(21.5);
        point.ColderCommand.Execute(null);
        point.ColderCommand.Execute(null);
        point.Temperature.Should().Be(20.5);

        point.Temperature = 30.5;
        point.WarmerCommand.Execute(null);
        point.Temperature.Should().Be(30.5);
        point.Temperature = 4.5;
        point.ColderCommand.Execute(null);
        point.Temperature.Should().Be(4.5);
    }

    [Fact]
    public void The_day_end_marker_and_the_day_borders_cannot_be_shifted_out_of_range()
    {
        var end = new SwitchPointViewModel { UntilText = "00:00" };
        end.LaterCommand.Execute(null);
        end.UntilText.Should().Be("00:00");

        var early = new SwitchPointViewModel { UntilText = "00:05" };
        early.EarlierCommand.Execute(null);
        early.UntilText.Should().Be("00:05");

        var late = new SwitchPointViewModel { UntilText = "23:55" };
        late.LaterCommand.Execute(null);
        late.UntilText.Should().Be("23:55");

        var broken = new SwitchPointViewModel { UntilText = "kaputt" };
        broken.LaterCommand.Execute(null);
        broken.UntilText.Should().Be("kaputt");
    }

    // ---------------------------------------------------------------------------------

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCore : ICubeGateway
    {
        public Cube? CubeToReturn { get; set; }
        public List<(int RoomId, Temperature SetPoint)> TemperatureCalls { get; } = new();
        public List<(int RoomId, DayOfWeek Day)> ProfileCalls { get; } = new();
        public CubeConnectionState State { get; private set; } = CubeConnectionState.Disconnected;

        public event EventHandler<CubeConnectionState>? StateChanged;
        public event EventHandler<Cube>? CubeRefreshed { add { } remove { } }
        public event EventHandler? SessionUnreachable { add { } remove { } }

        public Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            State = CubeConnectionState.Connected;
            StateChanged?.Invoke(this, State);
            return Task.FromResult(CubeToReturn!);
        }

        public Task SetRoomTemperatureAsync(
            int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default)
        {
            TemperatureCalls.Add((roomId, setPoint));
            return Task.CompletedTask;
        }

        public Task SetRoomControlModeAsync(
            int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
            DateTimeOffset? until, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetWeekDayProfileAsync(
            int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
        {
            ProfileCalls.Add((roomId, profile.Day));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            State = CubeConnectionState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdmin : ICubeAdminGateway
    {
        public List<string> Calls { get; } = new();
        public List<RoomConfigurationValues> Configurations { get; } = new();
        public List<ValveFunctionValues> ValveFunctions { get; } = new();
        public List<(RadioAddress Device, bool ShowActual)> Displays { get; } = new();
        public Queue<FoundDevice> Found { get; } = new();
        public List<string> Metadata { get; } = new();
        public List<PushButtonConfiguration> PushButtons { get; } = new();
        public List<(TimeZoneConfiguration Configuration, DateTimeOffset? Time)> TimeZones { get; } = new();
        public List<string[]> Ntp { get; } = new();

        public event EventHandler<AirtimeStatus>? AirtimeChanged;

        public void RaiseAirtime(AirtimeStatus status) => AirtimeChanged?.Invoke(this, status);

        public Task SetRoomConfigurationAsync(
            int roomId, RadioAddress master, RoomConfigurationValues values, CancellationToken cancellationToken = default)
        {
            Calls.Add($"config:{roomId}");
            Configurations.Add(values);
            return Task.CompletedTask;
        }

        public Task SetValveFunctionsAsync(
            int roomId, RadioAddress master, ValveFunctionValues values, CancellationToken cancellationToken = default)
        {
            ValveFunctions.Add(values);
            return Task.CompletedTask;
        }

        public Task SetDisplayModeAsync(
            RadioAddress wallThermostat, bool showActualTemperature, CancellationToken cancellationToken = default)
        {
            Displays.Add((wallThermostat, showActualTemperature));
            return Task.CompletedTask;
        }

        public Task<FoundDevice?> SearchNewDeviceAsync(TimeSpan window, CancellationToken cancellationToken = default)
            => Task.FromResult(Found.TryDequeue(out var found) ? found : null);

        public Task EndInclusionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetGroupAddressAsync(RadioAddress device, int groupId, CancellationToken cancellationToken = default)
        {
            Calls.Add($"group:{device}:{groupId}");
            return Task.CompletedTask;
        }

        public Task RemoveGroupAddressAsync(RadioAddress device, int roomId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetLinkPartnerAsync(
            RadioAddress device, RadioAddress partner, int partnerNumericType, bool add,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetLinkPartnerInRoomAsync(
            RadioAddress master, int roomId, RadioAddress partner, int partnerNumericType, bool add,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteDevicesAsync(
            IReadOnlyList<RadioAddress> devices, bool factoryReset, CancellationToken cancellationToken = default)
        {
            Calls.Add($"delete:{string.Join(',', devices)}:{(factoryReset ? "force" : "soft")}");
            return Task.CompletedTask;
        }

        public Task WriteMetadataAsync(Cube cube, CancellationToken cancellationToken = default)
        {
            Calls.Add("metadata");
            Metadata.Add(string.Join('|', cube.Rooms.Select(room => room.Name)));
            return Task.CompletedTask;
        }

        public Task<PushButtonConfiguration> ReadPushButtonAsync(RadioAddress cubeAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(new PushButtonConfiguration(
                new PushButtonKeyConfiguration(PushButtonKeyMode.Comfort, null),
                new PushButtonKeyConfiguration(PushButtonKeyMode.Eco, null)));

        public Task SetPushButtonAsync(
            RadioAddress cubeAddress, PushButtonConfiguration configuration, CancellationToken cancellationToken = default)
        {
            PushButtons.Add(configuration);
            return Task.CompletedTask;
        }

        public Task<TimeZoneConfiguration> ReadTimeZoneAsync(RadioAddress cubeAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);

        public Task SetTimeZoneAsync(
            RadioAddress cubeAddress, TimeZoneConfiguration configuration, DateTimeOffset? setTimeUtc,
            CancellationToken cancellationToken = default)
        {
            TimeZones.Add((configuration, setTimeUtc));
            return Task.CompletedTask;
        }

        public Task<string> QueryNtpServersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("F:pool.ntp.org");

        public Task SetNtpServersAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
        {
            Ntp.Add(servers.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNetwork : ICubeNetworkConfigurator
    {
        public List<CubeNetworkConfiguration> Written { get; } = new();

        public Task<CubeNetworkConfiguration> ReadAsync(string serialNumber, string ipAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(new CubeNetworkConfiguration(
                "192.168.178.3", "255.255.255.0", "192.168.178.1", "192.168.178.1", "8.8.8.8", true));

        public Task WriteAsync(
            string serialNumber, string ipAddress, CubeNetworkConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Written.Add(configuration);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : ICubeStateStore
    {
        public Dictionary<string, string> Settings { get; } = new();
        public List<string> Audit { get; } = new();

        public Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<CubeSnapshot?>(null);

        public Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings.TryGetValue(key, out var value) ? value : null);

        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Settings[key] = value;
            return Task.CompletedTask;
        }

        public Task AppendAuditAsync(string message, CancellationToken cancellationToken = default)
        {
            Audit.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Audit.ToArray());
    }
}
