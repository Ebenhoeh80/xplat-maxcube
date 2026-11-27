using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.UI.Localization;
using MaxCube.UI.Services;
using MaxCube.UI.ViewModels;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.UI.Tests;

public class ViewModelTests
{
    [Fact]
    public void Switch_point_parses_the_editable_time_text()
    {
        var point = new SwitchPointViewModel { UntilText = "18:30", Temperature = 19.5 };

        point.Until.Should().Be(new TimeOnly(18, 30));

        point.HasValidUntil.Should().BeTrue();

        point.UntilText = "ungültig";
        point.Until.Should().Be(TimeOnly.MinValue);
        point.HasValidUntil.Should().BeFalse();
    }

    [Fact]
    public void Day_profile_converts_to_domain_switch_points()
    {
        var day = new DayProfileViewModel(DayOfWeek.Saturday);
        day.Points.Add(new SwitchPointViewModel { UntilText = "06:00", Temperature = 20.5 });
        day.Points.Add(new SwitchPointViewModel { UntilText = "00:00", Temperature = 4.5 }); // 24:00

        var profile = day.ToProfile();

        profile.Day.Should().Be(DayOfWeek.Saturday);
        profile.Points.Should().HaveCount(2);
        profile.Points[0].Until.Should().Be(new TimeOnly(6, 0));
        profile.Points[0].Temperature.Should().Be(new Temperature(20.5));
        profile.Points[1].Until.Should().Be(TimeOnly.MinValue);
    }

    [Fact]
    public void A_day_accepts_at_most_13_switch_points()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        for (var index = 0; index < WeekTemperatureProfile.MaxPointsPerDay; index++)
        {
            day.AddPoint();
        }

        day.Points.Should().HaveCount(13);
        day.CanAddPoint.Should().BeFalse();
        day.AddPoint();
        day.Points.Should().HaveCount(13);

        day.RemovePoint(day.Points[0]);
        day.CanAddPoint.Should().BeTrue();
    }

    [Fact]
    public void Dragging_a_switch_point_snaps_to_the_grid_and_reorders()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        var morning = new SwitchPointViewModel { UntilText = "06:00", Temperature = 20.5 };
        var noon = new SwitchPointViewModel { UntilText = "12:00", Temperature = 21.0 };
        var end = new SwitchPointViewModel { UntilText = "00:00", Temperature = 21.0 };
        day.Points.Add(morning);
        day.Points.Add(noon);
        day.Points.Add(end);

        day.TryMovePoint(morning, new TimeOnly(13, 7)).Should().BeTrue();

        morning.UntilText.Should().Be("13:05");
        day.Points.Should().Equal(noon, morning, end);
        day.ToProfile().Points.Select(point => point.Until).Should().Equal(
            new TimeOnly(12, 0), new TimeOnly(13, 5), TimeOnly.MinValue);
    }

    [Fact]
    public void Dragging_rejects_duplicate_times_and_never_moves_the_end_point()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        var first = new SwitchPointViewModel { UntilText = "06:00", Temperature = 20.5 };
        var second = new SwitchPointViewModel { UntilText = "12:00", Temperature = 21.0 };
        var end = new SwitchPointViewModel { UntilText = "00:00", Temperature = 21.0 };
        day.Points.Add(first);
        day.Points.Add(second);
        day.Points.Add(end);

        day.TryMovePoint(first, new TimeOnly(12, 1)).Should().BeFalse("12:05 would duplicate 12:00");
        day.TryMovePoint(end, new TimeOnly(18, 0)).Should().BeFalse("00:00 is the fixed end of day");
        day.TryMovePoint(new SwitchPointViewModel { UntilText = "08:00" }, new TimeOnly(8, 0))
            .Should().BeFalse("ein Punkt, der nicht im Tag liegt, bleibt unberührt");

        day.Points.Should().Equal(first, second, end);
        first.UntilText.Should().Be("06:00");
    }

    [Fact]
    public void Main_viewmodel_passes_the_drop_time_to_the_day()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectedDay.Points.Insert(0,
            new SwitchPointViewModel { UntilText = "06:00", Temperature = 21.0 });
        var point = viewModel.SelectedDay.Points[0];

        viewModel.MoveSwitchPoint(point, new TimeOnly(7, 4)).Should().BeTrue();

        point.UntilText.Should().Be("07:05");
        viewModel.SelectedDay.Points.Should().ContainSingle(candidate => candidate.UntilText == "07:05");
    }

    [Fact]
    public void Room_and_device_viewmodels_reflect_the_domain()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        var room = new Room(1, "Wohnen", new RadioAddress(0x1D2A3C));
        var device = new Device(
            new RadioAddress(0x0A0B0C), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0001234"), "Thermostat", 1);
        cube.AddRoom(room);
        cube.AddDevice(device);
        cube.ApplyDeviceTelemetry(device.RadioAddress,
            DeviceStateInfo.Valid, RadioState.Ok, new Temperature(22.0));

        var roomViewModel = new RoomViewModel(room);
        var deviceViewModel = new DeviceViewModel(device);

        roomViewModel.Name.Should().Be("Wohnen");
        roomViewModel.SetPoint.Should().Be(22.0);
        roomViewModel.HasDeviceError.Should().BeFalse();
        deviceViewModel.IsHealthy.Should().BeTrue();
        deviceViewModel.Address.Should().Be("0A0B0C");
    }

    [Fact]
    public void Main_viewmodel_starts_with_seven_editable_days()
    {
        using var viewModel = CreateViewModel();

        viewModel.WeekDays.Should().HaveCount(7);
        viewModel.SelectedDay.Day.Should().Be(DayOfWeek.Monday);
        viewModel.SelectedDay.Points.Should().HaveCount(1);
        viewModel.StatusText.Should().Be("Getrennt");
    }

    [Fact]
    public async Task Main_viewmodel_shows_rooms_after_connecting()
    {
        using var viewModel = CreateViewModel(out _);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.IsConnected.Should().BeTrue();
        viewModel.Rooms.Should().ContainSingle();
        viewModel.SelectedRoom!.Name.Should().Be("Wohnen");
        viewModel.StatusText.Should().Contain("JEQ0001234");
    }

    [Fact]
    public async Task Commands_notify_when_connection_and_room_change()
    {
        using var viewModel = CreateViewModel();
        var notifications = 0;
        viewModel.ApplyTemperatureCommand.CanExecuteChanged += (_, _) => notifications++;
        viewModel.DisconnectCommand.CanExecute(null).Should().BeTrue(); // auch nach Fault erreichbar
        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeFalse();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeTrue();
        notifications.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_typed_temperature_survives_a_state_refresh_for_the_same_room()
    {
        using var viewModel = CreateViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.RequestedTemperature = 22.5;

        // Ein StateChanged nach „Auto“ darf die getippte Eingabe nicht überschreiben,
        // solange derselbe Raum ausgewählt bleibt (Sonnet N8/M-D).
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.RequestedTemperature.Should().Be(22.5);
    }

    [Fact]
    public async Task The_temperature_input_is_loaded_on_first_connect_and_after_reconnect()
    {
        using var viewModel = CreateViewModel(out var cube);
        cube.SetRoomTemperature(1, new Temperature(19.5));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.RequestedTemperature.Should().Be(19.5);

        // Sonnet N9: nach Disconnect ist previousId null; der Wert muss erneut
        // aus dem Raum geladen werden, nicht beim Default 21,0 bleiben.
        await viewModel.DisconnectCommand.ExecuteAsync(null);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.RequestedTemperature.Should().Be(19.5);
    }

    [Fact]
    public async Task Warnings_are_separate_from_status_and_cleared_by_the_next_action()
    {
        using var viewModel = CreateViewModel(out _, out var store);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        store.ThrowOnSave = true;

        await viewModel.ApplyTemperatureCommand.ExecuteAsync(null);
        viewModel.WarningText.Should().Be("Gesendet, aber lokaler Cache nicht aktualisiert.");
        viewModel.StatusText.Should().Contain("JEQ0001234");

        store.ThrowOnSave = false;
        await viewModel.ApplyTemperatureCommand.ExecuteAsync(null);
        viewModel.WarningText.Should().BeEmpty();

        await viewModel.DisconnectCommand.ExecuteAsync(null);
        viewModel.StatusText.Should().Be("Getrennt");
        viewModel.WarningText.Should().BeEmpty();
    }

    [Fact]
    public async Task Service_warnings_follow_the_selected_language()
    {
        using var viewModel = CreateViewModel(out _, out var store);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectedLanguage = viewModel.Languages.Single(language => language.Code == "fr");
        store.ThrowOnSave = true;

        await viewModel.ApplyTemperatureCommand.ExecuteAsync(null);

        viewModel.WarningText.Should().Be("Envoyé, mais le cache local n'a pas été mis à jour.");
    }

    [Theory]
    [InlineData("Timed out connecting to the cube.", "Zeitüberschreitung", "en", "Timed out connecting to the cube")]
    [InlineData("Cannot connect to cube at 192.168.10.60:62910.", "nicht erreichbar", "fr", "inaccessible")]
    [InlineData("No Incoming Hello", "Handshake", "pl", "uzgadniania")]
    [InlineData("H: line needs at least serial, radio address and firmware.", "ungültig", "nl", "ongeldig")]
    [InlineData("Firmware 999 is outside the supported range 273..275.", "nicht unterstützt", "it", "non supportato")]
    [InlineData("Already connected. Disconnect first.", "bereits eine Verbindung", "fr", "connecté")]
    public async Task Connect_errors_show_a_localized_cause_and_follow_the_language_switch(
        string rawFailure, string germanHint, string language, string switchedHint)
    {
        using var viewModel = CreateViewModelWithGateway(out _, out var gateway);
        viewModel.Host = "192.168.10.60";
        viewModel.Port = 62910;
        gateway.ConnectHook = _ => throw new InvalidOperationException(rawFailure);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.ErrorMessage.Should().Contain(germanHint).And.NotContain(rawFailure);
        viewModel.SelectedLanguage = viewModel.Languages.Single(item => item.Code == language);
        viewModel.ErrorMessage.Should().Contain(switchedHint).And.NotContain(rawFailure);
    }

    [Fact]
    public async Task Cancelling_restores_the_status_text()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = CreateViewModelWithGateway(out _, out var gateway);
        gateway.ConnectHook = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var connect = viewModel.ConnectCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var statusBefore = viewModel.StatusText;

        viewModel.CancelCommand.Execute(null);
        await connect;

        viewModel.SuccessText.Should().Be("Abgebrochen.");
        viewModel.StatusText.Should().Be(statusBefore);
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void Automatic_reconnect_is_visible_in_the_status()
    {
        // Gremium Runde 3, Prüfung: Der Service meldet auch den automatischen
        // Reconnect. Die UI muss ihn als verbunden anzeigen, sonst bleiben die
        // Aktionen gesperrt und ein erneuter Connect läuft in „Already connected“.
        var gateway = new FakeGateway();
        var clock = new FakeClock();
        var service = new CubeApplicationService(gateway, new FakeStore(), clock);
        using var viewModel = new MainWindowViewModel(service, clock);

        gateway.RaiseFaulted();
        viewModel.IsConnected.Should().BeFalse();
        viewModel.StatusText.Should().Be("Verbindung unterbrochen");

        gateway.RaiseConnected();

        viewModel.IsConnected.Should().BeTrue();
        viewModel.StatusText.Should().Be("Verbindung wiederhergestellt");
    }

    [Fact]
    public async Task Selecting_another_room_refreshes_the_device_list()
    {
        using var viewModel = CreateViewModelWithGateway(out var cube, out _);
        cube.AddRoom(new Room(2, "Küche", new RadioAddress(0x1D2A3D)));
        cube.AddDevice(new Device(
            new RadioAddress(0x0A0B0D), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0005678"), "Küchenthermostat", 2));

        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Devices.Should().ContainSingle().Which.Address.Should().Be("0A0B0C");

        viewModel.SelectedDevicesRoom = viewModel.Rooms.First(room => room.Id == 2);

        viewModel.Devices.Should().ContainSingle().Which.Address.Should().Be("0A0B0D");
    }

    [Fact]
    public async Task Week_profile_send_is_blocked_until_the_selected_day_was_read()
    {
        // Gremium Runde 2: Der Button hängt am konkreten Wochentag, nicht nur am Raum.
        using var blocked = CreateViewModel(out _);
        await blocked.ConnectCommand.ExecuteAsync(null);
        blocked.SaveWeekProfileCommand.CanExecute(null).Should().BeFalse();

        var profile = new WeekTemperatureProfile();
        profile.SetDay(new DayProfile(DayOfWeek.Monday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(21.0)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(21.0)),
        ]));
        using var viewModel = CreateViewModel(out var cube);
        cube.ApplyWeekProfile(1, profile);
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.SelectedDay.Day.Should().Be(DayOfWeek.Monday);
        viewModel.SaveWeekProfileCommand.CanExecute(null).Should().BeTrue();
        viewModel.SelectedDay = viewModel.WeekDays.First(day => day.Day == DayOfWeek.Tuesday);
        viewModel.SaveWeekProfileCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void An_unfinished_day_profile_is_rejected_before_sending()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        day.Points.Add(new SwitchPointViewModel { UntilText = "06:00", Temperature = 20.5 });

        var act = () => day.ToProfile();

        act.Should().Throw<MaxCube.Domain.Exceptions.DomainValidationException>()
            .WithMessage("*24:00*");
    }

    [Fact]
    public async Task Background_connection_events_are_marshalled_to_the_ui_thread()
    {
        var gateway = new FakeGateway();
        var clock = new FakeClock();
        var service = new CubeApplicationService(gateway, new FakeStore(), clock);
        var dispatcher = new RecordingUiDispatcher();
        using var viewModel = new MainWindowViewModel(service, clock, dispatcher);

        await Task.Run(() => gateway.RaiseFaulted(), TestContext.Current.CancellationToken);

        dispatcher.Posted.Should().Be(1);
        // Post wurde nur aufgezeichnet, daher kein direkter Property-Zugriff vom Hintergrundthread.
        viewModel.StatusText.Should().Be("Getrennt");
    }

    [Fact]
    public void App_version_comes_from_the_assembly_and_is_visible_in_title_and_status()
    {
        var provider = new AppVersionProvider();

        using var viewModel = CreateViewModel(out _, out _, out _, versionProvider: provider);

        provider.Version.Should().NotBeNullOrWhiteSpace();
        viewModel.AppVersion.Should().Be(provider.Version);
        viewModel.WindowTitle.Should().Be($"MAX! Cube – Lokale App {provider.Version}");
    }

    [Fact]
    public async Task Discovery_fills_the_host_and_uses_the_physical_cube_port()
    {
        var discovery = new FakeDiscovery(
            new DiscoveredCube("JEQ0001234", 275, false, "192.168.178.3"));
        var clock = new FakeClock();
        var gateway = new FakeGateway
        {
            CubeToReturn = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true),
        };
        using var viewModel = new MainWindowViewModel(
            new CubeApplicationService(gateway, new FakeStore(), clock),
            clock,
            discovery: discovery);

        await viewModel.DiscoverCubesCommand.ExecuteAsync(null);

        viewModel.DiscoveredCubes.Should().ContainSingle()
            .Which.Display.Should().Contain("JEQ0001234").And.Contain("192.168.178.3");
        viewModel.UseDiscoveredCubeCommand.Execute(null);
        viewModel.Host.Should().Be("192.168.178.3");
        viewModel.Port.Should().Be(62910);
        viewModel.ConnectCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.178.255", true)]
    [InlineData("127.1", false)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.2.3", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("1.2.3.a", false)]
    public void Manual_host_requires_exactly_four_decimal_octets(string host, bool expected)
    {
        using var viewModel = CreateViewModel(out _, out _, out _);

        viewModel.Host = host;

        viewModel.ConnectCommand.CanExecute(null).Should().Be(expected);
    }

    [Fact]
    public async Task Last_endpoint_is_persisted_and_can_be_restored()
    {
        using var viewModel = CreateViewModel(out _, out var store, out _);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        store.Settings["cube.endpoint"].Should().Be("127.0.0.1:62910");

        viewModel.Host = "0.0.0.0";
        await viewModel.LoadLastEndpointCommand.ExecuteAsync(null);

        viewModel.Host.Should().Be("127.0.0.1");
        viewModel.Port.Should().Be(62910);
    }

    [Fact]
    public async Task House_control_only_sends_to_selected_rooms()
    {
        using var viewModel = CreateViewModelWithGateway(out var cube, out var gateway);
        cube.AddRoom(new Room(2, "Küche", new RadioAddress(0x1D2A3D)));
        await viewModel.ConnectCommand.ExecuteAsync(null);

        await viewModel.SetHouseAutoCommand.ExecuteAsync(null);
        gateway.ModeCalls.Should().BeEmpty();

        viewModel.Rooms.First(room => room.Id == 1).IsHouseSelected = true;
        await viewModel.SetHouseEcoCommand.ExecuteAsync(null);

        gateway.TemperatureCalls.Should().ContainSingle()
            .Which.SetPoint.Should().Be(new Temperature(16.5));
    }

    [Fact]
    public async Task Single_room_temporary_uses_the_entered_end_time()
    {
        using var viewModel = CreateViewModelWithGateway(out _, out var gateway);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        var until = new DateTimeOffset(2026, 11, 24, 22, 30, 0, TimeSpan.Zero);
        viewModel.TemporaryEnd = until;

        await viewModel.SetTemporaryCommand.ExecuteAsync(null);

        gateway.ModeCalls.Should().ContainSingle()
            .Which.Mode.Should().Be(RoomControlMode.Temporary);
        gateway.UntilCalls.Should().ContainSingle().Which.Should().Be(until);
    }

    [Fact]
    public async Task Copying_a_day_only_overwrites_days_that_were_read_from_the_cube()
    {
        using var viewModel = CreateViewModel(out var cube, out _, out var gateway);
        var profile = new WeekTemperatureProfile();
        profile.SetDay(new DayProfile(DayOfWeek.Monday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(21.0)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(21.0)),
        ]));
        cube.ApplyWeekProfile(1, profile);
        await viewModel.ConnectCommand.ExecuteAsync(null);

        await viewModel.CopyDayToWeekCommand.ExecuteAsync(null);

        viewModel.IsWritePreviewVisible.Should().BeTrue();
        viewModel.WritePreviewText.Should().Contain("Wohnen");
        gateway.ProfileCalls.Should().BeEmpty("vor der Bestätigung darf nichts gesendet werden");

        await viewModel.ConfirmWriteCommand.ExecuteAsync(null);

        gateway.ProfileCalls.Should().ContainSingle()
            .Which.Day.Should().Be(DayOfWeek.Monday);
        viewModel.SuccessText.Should().Be("Profil auf 1 Tag(e) kopiert.");
        viewModel.IsWritePreviewVisible.Should().BeFalse();
    }

    [Fact]
    public void Data_status_start_unread_and_never_claim_live_without_a_read()
    {
        using var status = new DataStatusViewModel(new Localizer("de"));

        status.State.Should().Be(DataReadState.NotRead);
        status.HasRead.Should().BeFalse();
        status.IsNotRead.Should().BeTrue();
        status.Text.Should().StartWith("○");
        status.Text.Should().Contain("Nicht vom Cube gelesen");

        status.Update(DataReadState.Live, null);
        status.HasRead.Should().BeFalse("ein Live-Text ohne Lesung wäre irreführend");
    }

    [Fact]
    public void Data_status_text_is_localized_without_raw_keys_in_all_six_languages()
    {
        foreach (var language in Localizer.Languages.Select(item => item.Code))
        {
            using var status = new DataStatusViewModel(new Localizer(language));
            foreach (var state in Enum.GetValues<DataReadState>())
            {
                status.Update(state, new DateTimeOffset(2026, 11, 25, 10, 30, 0, TimeSpan.Zero), "Test");
                status.Text.Should().NotContain("{0}");
                status.Text.Should().NotContain("Data");
                status.Text.Should().NotBeEmpty();
                status.Glyph.Should().BeOneOf("○", "◌", "●", "◐", "⚠");
                status.Text.Should().StartWith(status.Glyph);
            }
        }
    }

    [Fact]
    public void Weekday_placeholder_is_never_a_read_profile()
    {
        var day = new DayProfileViewModel(DayOfWeek.Monday);
        day.WasReadFromCube.Should().BeFalse();

        day.Reset();
        day.WasReadFromCube.Should().BeFalse("der Default 00:00/21.0 ist kein Cube-Programm");

        day.Load(new DayProfile(DayOfWeek.Monday, [new SwitchPoint(new TimeOnly(6, 0), new Temperature(19.0))]));
        day.WasReadFromCube.Should().BeTrue();
    }

    [Fact]
    public async Task Writes_are_blocked_until_values_were_read_from_the_cube()
    {
        using var viewModel = CreateViewModel();

        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeFalse();
        viewModel.SetHouseAutoCommand.CanExecute(null).Should().BeFalse();
        viewModel.SaveWeekProfileCommand.CanExecute(null).Should().BeFalse();
        viewModel.RoomsStatus.HasRead.Should().BeFalse();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.RoomsStatus.State.Should().Be(DataReadState.Live);
        viewModel.RoomsStatus.HasRead.Should().BeTrue();
        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeTrue();
        // Der Test-Cube liefert kein Wochenprogramm: der sichtbare Default bleibt ein
        // Platzhalter und darf auch nach dem Verbinden nicht gesendet werden.
        viewModel.WeekStatus.HasRead.Should().BeFalse();
        viewModel.SaveWeekProfileCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Activating_the_cube_tab_reads_it_and_shows_live_data()
    {
        using var viewModel = CreateViewModel(out _, out _, out var gateway);
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.CubeInfoStatus.State.Should().Be(DataReadState.NotRead);
        viewModel.SelectedTabIndex = TabIndex.Cube;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        viewModel.CubeInfoStatus.State.Should().Be(DataReadState.Live);
        viewModel.CubeInfoStatus.Text.Should().Contain("●");
        gateway.CubeToReturn.Should().NotBeNull();
    }

    [Fact]
    public void About_page_exposes_author_license_and_project_without_runtime_downloads()
    {
        using var viewModel = CreateViewModel();

        MainWindowViewModel.AuthorName.Should().Be("Ebenhoeh80");
        MainWindowViewModel.LicenseName.Should().Be("MIT");
        MainWindowViewModel.ProjectUrl.Should().Be("https://github.com/Ebenhoeh80/xplat-maxcube");
        viewModel.LicenseFullText.Should().Contain("MIT License").And.Contain("Permission is hereby granted");
        viewModel.AboutDisclaimerText.Should().Contain("eQ-3");
        viewModel.OpenProjectUrlCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Project_link_without_a_browser_reports_the_url_instead_of_crashing()
    {
        var opener = new FakeUrlOpener(succeeds: false);
        using var viewModel = CreateViewModel(urlOpener: opener);

        viewModel.OpenProjectUrlCommand.Execute(null);

        opener.Target.Should().Be(MainWindowViewModel.ProjectUrl);
        viewModel.StatusText.Should().Contain(MainWindowViewModel.ProjectUrl);
    }

    [Fact]
    public void Project_link_with_a_browser_leaves_the_status_untouched()
    {
        var opener = new FakeUrlOpener(succeeds: true);
        using var viewModel = CreateViewModel(urlOpener: opener);
        var before = viewModel.StatusText;

        viewModel.OpenProjectUrlCommand.Execute(null);

        opener.Target.Should().Be(MainWindowViewModel.ProjectUrl);
        viewModel.StatusText.Should().Be(before);
    }

    [Fact]
    public void Status_bar_is_honest_before_the_first_read()
    {
        using var viewModel = CreateViewModel();

        viewModel.StatusBarConnectionText.Should().StartWith("○");
        viewModel.StatusBarConnectionText.Should().Contain("Getrennt");
        viewModel.StatusBarDataText.Should().Contain("Noch nicht vom Cube gelesen");
        viewModel.StatusBarActionText.Should().Contain("Keine Aktion");
        viewModel.StatusBarLogText.Should().Contain("Protokoll aus");
    }

    [Fact]
    public async Task Status_bar_tracks_live_data_and_last_action_with_a_time()
    {
        using var viewModel = CreateViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.StatusBarConnectionText.Should().StartWith("●");
        viewModel.StatusBarDataText.Should().Contain("Letztes Lesen:");
        viewModel.StatusBarActionText.Should().Contain("Verbindung hergestellt").And.Contain(":00:00");

        viewModel.SuccessText = "Temperatur gesendet.";
        viewModel.StatusBarActionText.Should().Contain("Temperatur gesendet.");
    }

    [Fact]
    public async Task Log_switch_is_opt_in_and_reaches_the_store_without_restart()
    {
        using var viewModel = CreateViewModelWithLog(out _, out var log);
        log.IsEnabled.Should().BeFalse();

        await viewModel.InitializeAsync();
        log.IsEnabled.Should().BeFalse();
        log.Entries.Should().BeEmpty();

        viewModel.LogEnabled = true;
        log.IsEnabled.Should().BeTrue();
        log.Entries.Should().Contain(entry => entry.Contains("Protokoll eingeschaltet"));
        viewModel.StatusBarLogText.Should().Contain("Protokoll an");
        viewModel.LogText.Should().Contain("Protokoll eingeschaltet");
    }

    [Fact]
    public async Task Cached_values_are_not_writable_while_connected()
    {
        using var viewModel = CreateViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeTrue();

        viewModel.RoomsStatus.Update(DataReadState.Cached, DateTimeOffset.UtcNow);

        viewModel.ApplyTemperatureCommand.CanExecute(null).Should().BeFalse();
        viewModel.SetHouseAutoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Saved_endpoint_is_used_at_startup()
    {
        using var viewModel = CreateViewModel(out _, out var store);
        store.Settings["cube.endpoint"] = "192.168.10.60:62910";

        await viewModel.InitializeAsync();

        viewModel.Host.Should().Be("192.168.10.60");
        viewModel.Port.Should().Be(62910);
    }

    [Fact]
    public async Task Service_events_from_a_background_thread_go_through_the_ui_dispatcher()
    {
        var dispatcher = new CountingUiDispatcher();
        using var viewModel = CreateViewModel(out _, out _, out var gateway, dispatcher: dispatcher);
        await viewModel.ConnectCommand.ExecuteAsync(null);
        var before = dispatcher.Posted;

        await Task.Run(() => gateway.RaiseFaulted(), TestContext.Current.CancellationToken);

        dispatcher.Posted.Should().BeGreaterThan(before);
        viewModel.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Successful_actions_log_the_success_text_not_the_failure_prefix()
    {
        using var viewModel = CreateViewModelWithLog(out _, out var log);
        viewModel.LogEnabled = true;
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.RequestedTemperature = 20.5;

        await viewModel.ApplyTemperatureCommand.ExecuteAsync(null);

        log.Entries.Should().Contain("[INFO] Temperatur gesendet.");
        log.Entries.Should().NotContain(entry =>
            entry.Contains("Temperatur konnte nicht gesendet werden: Temperatur gesendet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Switching_the_log_off_writes_a_final_entry()
    {
        using var viewModel = CreateViewModelWithLog(out _, out var log);
        await viewModel.InitializeAsync();

        viewModel.LogEnabled = true;
        viewModel.LogEnabled = false;

        log.Entries.Should().Contain("[INFO] Protokoll eingeschaltet.");
        log.Entries.Should().Contain("[INFO] Protokoll ausgeschaltet.");
    }

    [Fact]
    public void A_discarded_radiogram_gets_an_actionable_hint()
    {
        ErrorCatalog.Describe("Konfiguration: Command discarded", german: true)
            .Should().Contain("Funkauslastung");
        ErrorCatalog.Describe("Command discarded", german: false)
            .Should().Contain("airtime");
    }

    [Fact]
    public void The_ui_viewmodel_does_not_opt_out_of_the_ui_synchronization_context()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MaxCube.UI")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("die Quellen sind im Testlauf immer vorhanden");
        var uiSources = Path.Combine(root!.FullName, "src", "MaxCube.UI");
        var offenders = Directory.GetFiles(uiSources, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(item => item.line.Contains("ConfigureAwait(false)", StringComparison.Ordinal))
            .Select(item => $"{Path.GetFileName(item.file)}:{item.number}")
            .ToArray();

        offenders.Should().BeEmpty("UI-Fortsetzungen müssen auf dem UI-Thread landen");
    }

    [Fact]
    public void The_tray_exit_uses_the_graceful_shutdown_path()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && root.GetFiles("MaxCube.slnx").Length == 0)
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("die Quellen sind im Testlauf immer vorhanden");
        var app = File.ReadAllText(Path.Combine(
            root!.FullName, "src", "MaxCube.UI.Desktop", "App.axaml.cs"));

        app.Should().Contain("exit.Click += (_, _) => desktop.TryShutdown();");
        app.Should().NotContain("exit.Click += (_, _) => desktop.Shutdown();",
            "ein forcierter Exit überspringt ShutdownRequested und damit q:");
    }

    private sealed class FakeDiscovery : ICubeDiscovery
    {
        private readonly IReadOnlyList<DiscoveredCube> _cubes;

        public FakeDiscovery(params DiscoveredCube[] cubes) => _cubes = cubes;

        public Task<IReadOnlyList<DiscoveredCube>> DiscoverAsync(
            TimeSpan duration, CancellationToken cancellationToken = default)
            => Task.FromResult(_cubes);
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int Posted { get; private set; }

        public bool CheckAccess() => false;

        public void Post(Action action) => Posted++;
    }

    /// <summary>Simuliert Avalonia: jeder fremdthreads Event muss über Post laufen.</summary>
    private sealed class CountingUiDispatcher : IUiDispatcher
    {
        public int Posted { get; private set; }

        public bool CheckAccess() => false;

        public void Post(Action action)
        {
            Posted++;
            action();
        }
    }

    private static MainWindowViewModel CreateViewModel() => CreateViewModel(out _, out _);

    private static MainWindowViewModel CreateViewModel(IUrlOpener urlOpener)
        => CreateViewModel(out _, out _, out _, urlOpener: urlOpener);

    private static MainWindowViewModel CreateViewModel(out Cube cube) => CreateViewModel(out cube, out _);

    private static MainWindowViewModel CreateViewModelWithGateway(out Cube cube, out FakeGateway gateway)
        => CreateViewModel(out cube, out _, out gateway);

    private static MainWindowViewModel CreateViewModelWithLog(out Cube cube, out NullLogStore log)
    {
        log = new NullLogStore();
        return CreateViewModel(out cube, out _, out _, logStore: log);
    }

    private static MainWindowViewModel CreateViewModel(out Cube cube, out FakeStore store)
        => CreateViewModel(out cube, out store, out _);

    private static MainWindowViewModel CreateViewModel(
        out Cube cube,
        out FakeStore store,
        out FakeGateway gateway,
        ICubeDiscovery? discovery = null,
        IAppVersionProvider? versionProvider = null,
        ILogStore? logStore = null,
        IUiDispatcher? dispatcher = null,
        IUrlOpener? urlOpener = null)
    {
        cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0x1D2A3C)));
        cube.AddDevice(new Device(
            new RadioAddress(0x0A0B0C), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0001234"), "Thermostat", 1));

        gateway = new FakeGateway { CubeToReturn = cube };
        store = new FakeStore();
        var clock = new FakeClock();
        var service = new CubeApplicationService(gateway, store, clock);
        return new MainWindowViewModel(service, clock, discovery: discovery, dispatcher: dispatcher,
            versionProvider: versionProvider, logStore: logStore, urlOpener: urlOpener);
    }

    private sealed class FakeUrlOpener(bool succeeds) : IUrlOpener
    {
        public string? Target { get; private set; }

        public bool TryOpen(string target)
        {
            Target = target;
            return succeeds;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeGateway : ICubeGateway
    {
        public Cube? CubeToReturn { get; set; }
        public Func<CancellationToken, Task>? ConnectHook { get; set; }
        public CubeConnectionState State { get; private set; } = CubeConnectionState.Disconnected;
        public List<(int RoomId, Temperature SetPoint)> TemperatureCalls { get; } = [];
        public List<(int RoomId, RoomControlMode Mode)> ModeCalls { get; } = [];
        public List<DayProfile> ProfileCalls { get; } = [];

        public event EventHandler<CubeConnectionState>? StateChanged;
        public event EventHandler<Cube>? CubeRefreshed { add { } remove { } }
        public event EventHandler? SessionUnreachable { add { } remove { } }

        public async Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            if (ConnectHook is not null)
            {
                await ConnectHook(cancellationToken).ConfigureAwait(false);
            }

            State = CubeConnectionState.Connected;
            StateChanged?.Invoke(this, State);
            return CubeToReturn ?? throw new InvalidOperationException("No cube configured.");
        }

        public Task SetRoomTemperatureAsync(
            int roomId, RadioAddress master, Temperature setPoint, CancellationToken cancellationToken = default)
        {
            TemperatureCalls.Add((roomId, setPoint));
            return Task.CompletedTask;
        }

        public List<DateTimeOffset?> UntilCalls { get; } = [];

        public Task SetRoomControlModeAsync(
            int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
            DateTimeOffset? until, CancellationToken cancellationToken = default)
        {
            ModeCalls.Add((roomId, mode));
            UntilCalls.Add(until);
            return Task.CompletedTask;
        }

        public Task SetWeekDayProfileAsync(
            int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
        {
            ProfileCalls.Add(profile);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            State = CubeConnectionState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public void RaiseFaulted()
        {
            State = CubeConnectionState.Faulted;
            StateChanged?.Invoke(this, State);
        }

        public void RaiseConnected()
        {
            State = CubeConnectionState.Connected;
            StateChanged?.Invoke(this, State);
        }
    }

    private sealed class FakeStore : ICubeStateStore
    {
        public bool ThrowOnSave { get; set; }
        public Dictionary<string, string> Settings { get; } = new();

        public Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<CubeSnapshot?>(null);

        public Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default)
            => ThrowOnSave
                ? Task.FromException(new IOException("Simulierter Cachefehler."))
                : Task.CompletedTask;

        public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Settings.TryGetValue(key, out var value) ? value : null);

        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Settings[key] = value;
            return Task.CompletedTask;
        }

        public Task AppendAuditAsync(
            string message, AuditSource source = AuditSource.User, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteAuditOlderThanAsync(
            DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
