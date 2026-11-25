using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.UI.Localization;
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
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

        viewModel.SelectedRoom = viewModel.Rooms.First(room => room.Id == 2);

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

        await Task.Run(() => gateway.RaiseFaulted());

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

        gateway.ProfileCalls.Should().ContainSingle()
            .Which.Day.Should().Be(DayOfWeek.Monday);
        viewModel.SuccessText.Should().Be("Profil auf 1 Tag(e) kopiert.");
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
        await Task.Delay(50);

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

    private static MainWindowViewModel CreateViewModel() => CreateViewModel(out _, out _);

    private static MainWindowViewModel CreateViewModel(out Cube cube) => CreateViewModel(out cube, out _);

    private static MainWindowViewModel CreateViewModelWithGateway(out Cube cube, out FakeGateway gateway)
        => CreateViewModel(out cube, out _, out gateway);

    private static MainWindowViewModel CreateViewModel(out Cube cube, out FakeStore store)
        => CreateViewModel(out cube, out store, out _);

    private static MainWindowViewModel CreateViewModel(
        out Cube cube,
        out FakeStore store,
        out FakeGateway gateway,
        ICubeDiscovery? discovery = null,
        IAppVersionProvider? versionProvider = null)
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
        return new MainWindowViewModel(service, clock, discovery: discovery, versionProvider: versionProvider);
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

        public Task SetRoomControlModeAsync(
            int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
            DateTimeOffset? until, CancellationToken cancellationToken = default)
        {
            ModeCalls.Add((roomId, mode));
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

        public Task AppendAuditAsync(string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ReadAuditAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
