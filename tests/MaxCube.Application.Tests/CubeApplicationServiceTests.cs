using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Application.Tests;

public class CubeApplicationServiceTests
{
    private static Cube BuildCube() => BuildCube(out _);

    private static Cube BuildCubeWithProfile()
    {
        var cube = BuildCube();
        var profile = new WeekTemperatureProfile();
        profile.SetDay(new DayProfile(DayOfWeek.Saturday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(20.0)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(17.0)),
        ]));
        cube.ApplyWeekProfile(1, profile);
        return cube;
    }

    private static Cube BuildCube(out Room room)
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        room = new Room(1, "Wohnen", new RadioAddress(0x1D2A3C));
        cube.AddRoom(room);
        cube.AddDevice(new Device(
            new RadioAddress(0x0A0B0C), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0001234"), "Thermostat", 1));
        return cube;
    }

    [Fact]
    public void The_system_clock_keeps_the_local_offset_for_temporary_end_times()
    {
        var now = new SystemClock().Now;

        // RadiogramSpecification.java:187-191 nutzt Calendar.getInstance() (Ortszeit).
        now.Offset.Should().Be(TimeZoneInfo.Local.GetUtcOffset(now.UtcDateTime));
        now.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Connect_publishes_state_and_persists_the_cache()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var store = new FakeStore();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        var events = new List<Cube>();
        service.StateChanged += (_, cube) => events.Add(cube);

        var cube = await service.ConnectAsync(new CubeEndpoint("192.168.1.5", 62910));

        cube.Should().BeSameAs(gateway.CubeToReturn);
        service.Current.Should().BeSameAs(cube);
        service.ConnectionState.Should().Be(CubeConnectionState.Connected);
        events.Should().ContainSingle().Which.Should().BeSameAs(cube);
        store.Settings["cube.serial"].Should().Be("JEQ0001234");
        store.Snapshot.Should().NotBeNull();
        store.Snapshot!.Rooms.Should().ContainSingle();
        store.Audit.Should().Contain(text => text.Contains("192.168.1.5:62910"));
        gateway.Calls.Should().Contain("connect:192.168.1.5:62910");
    }

    [Fact]
    public async Task Setting_temperature_updates_the_aggregate_and_the_cache()
    {
        var cube = BuildCube(out var room);
        var gateway = new FakeGateway { CubeToReturn = cube };
        var store = new FakeStore();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        await service.SetRoomTemperatureAsync(1, new Temperature(22.5));

        gateway.LastTemperature.Should().Be(new Temperature(22.5));
        room.SetPointTemperature.Should().Be(new Temperature(22.5));
        store.Snapshot!.Rooms[0].SetPoint.Should().Be(22.5);
    }

    [Fact]
    public async Task Setpoints_are_quantised_to_the_wire_grid()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        await service.SetRoomTemperatureAsync(1, new Temperature(21.3));

        gateway.LastTemperature.Should().Be(new Temperature(21.5));
        service.Current!.GetRoom(1)!.SetPointTemperature.Should().Be(new Temperature(21.5));
    }

    [Fact]
    public async Task Control_mode_setpoints_are_quantised_too()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        await service.SetRoomControlModeAsync(1, RoomControlMode.Permanently, new Temperature(20.3), null);
        gateway.LastTemperature.Should().Be(new Temperature(20.5));

        await service.SetRoomControlModeAsync(
            1, RoomControlMode.Temporary, new Temperature(20.3),
            new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero));
        gateway.LastTemperature.Should().Be(new Temperature(20.5));
    }

    [Fact]
    public async Task Eco_and_comfort_use_the_belegten_room_setpoints()
    {
        var cube = BuildCube(out var room);
        var gateway = new FakeGateway { CubeToReturn = cube };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        await service.ApplyEcoAsync(1);
        gateway.LastTemperature.Should().Be(room.EcoTemperature);

        await service.ApplyComfortAsync(1);
        gateway.LastTemperature.Should().Be(room.ComfortTemperature);
    }

    [Fact]
    public async Task Temporary_mode_requires_setpoint_and_end_time()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        var withoutSetpoint = () => service.SetRoomControlModeAsync(1, RoomControlMode.Permanently, null, null);
        var withoutUntil = () => service.SetRoomControlModeAsync(
            1, RoomControlMode.Temporary, new Temperature(20.0), null);

        (await withoutSetpoint.Should().ThrowAsync<MaxCube.Domain.Exceptions.DomainValidationException>())
            .WithMessage("*setpoint*");
        (await withoutUntil.Should().ThrowAsync<MaxCube.Domain.Exceptions.DomainValidationException>())
            .WithMessage("*end time*");
    }

    [Fact]
    public async Task Temporary_mode_end_time_must_be_in_the_future()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        var inThePast = () => service.SetRoomControlModeAsync(
            1, RoomControlMode.Temporary, new Temperature(20.0),
            new DateTimeOffset(2026, 11, 24, 11, 0, 0, TimeSpan.Zero));
        var inTheFuture = () => service.SetRoomControlModeAsync(
            1, RoomControlMode.Temporary, new Temperature(20.0),
            new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero));

        (await inThePast.Should().ThrowAsync<MaxCube.Domain.Exceptions.DomainValidationException>())
            .WithMessage("*future*");
        await inTheFuture.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cache_failure_after_sending_reports_a_warning_but_keeps_the_state()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var store = new FakeStore();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        var warnings = new List<string>();
        var states = 0;
        service.Warning += (_, message) => warnings.Add(message);
        service.StateChanged += (_, _) => states++;
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        warnings.Clear();
        store.FailSave = true;

        await service.SetRoomTemperatureAsync(1, new Temperature(22.0));

        gateway.Calls.Should().Contain("temperature:1");
        service.Current!.GetRoom(1)!.SetPointTemperature.Should().Be(new Temperature(22.0));
        states.Should().BeGreaterThan(0);
        warnings.Should().ContainSingle().Which.Should().Contain("Cache");
    }

    [Fact]
    public async Task Throwing_state_listener_reports_a_warning_instead_of_crashing()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        var warnings = new List<string>();
        service.Warning += (_, message) => warnings.Add(message);
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        // Nur der asynchrone Live-Pfad ist hier zu prüfen; ConnectAsync meldet den Zustand
        // synchron und lasst eine Ausnahme bewusst an den Aufrufer durchlaufen.
        service.StateChanged += (_, _) => throw new InvalidOperationException("Listener fehlerhaft");
        warnings.Clear();

        gateway.RaiseCubeRefreshed(BuildCube());
        await Task.Delay(50);

        warnings.Should().Contain(message => message.Contains("Live-Zustand konnte nicht gemeldet"));
    }

    [Fact]
    public async Task Cached_state_can_be_loaded_after_the_gateway_faulted()
    {
        var cube = BuildCube();
        var source = new FakeStore();
        await new CubeApplicationService(new FakeGateway { CubeToReturn = cube }, source, new FakeClock())
            .ConnectAsync(new CubeEndpoint("cube", 62910));
        var gateway = new FakeGateway();
        gateway.RaiseFaulted();
        var service = new CubeApplicationService(
            gateway, new FakeStore { Settings = source.Settings, Snapshot = source.Snapshot }, new FakeClock());

        var restored = await service.LoadCachedAsync();

        restored.Should().NotBeNull();
        service.IsOffline.Should().BeTrue();
    }

    [Fact]
    public async Task Week_profile_is_forwarded_as_one_day_profile()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCubeWithProfile() };
        var store = new FakeStore();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        var profile = new DayProfile(DayOfWeek.Saturday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(20.5)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(4.5)), // 24:00
        ]);

        await service.SetWeekDayProfileAsync(1, profile);

        gateway.LastProfile.Should().BeSameAs(profile);
        store.Audit.Should().Contain(text => text.Contains("Wochenprofil 1/Saturday"));
    }

    [Fact]
    public async Task Sending_a_week_profile_without_a_cube_profile_is_blocked()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        var profile = new DayProfile(DayOfWeek.Monday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(20.0)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(17.0)),
        ]);

        var act = () => service.SetWeekDayProfileAsync(1, profile);

        (await act.Should().ThrowAsync<MaxCube.Domain.Exceptions.DomainValidationException>())
            .WithMessage("*nicht vom Cube gelesen*");
        gateway.LastProfile.Should().BeNull();
        service.HasWeekProfile(1).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_a_day_not_read_from_the_cube_is_blocked()
    {
        // Gremium Runde 2: Der Profil ist pro Raum geladen, aber nur Saturday wurde
        // tatsächlich dekodiert. Ein Default-Montag darf das echte Programm nicht überschreiben.
        var gateway = new FakeGateway { CubeToReturn = BuildCubeWithProfile() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        var monday = new DayProfile(DayOfWeek.Monday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(21.0)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(21.0)),
        ]);

        var act = () => service.SetWeekDayProfileAsync(1, monday);

        (await act.Should().ThrowAsync<MaxCube.Domain.Exceptions.DomainValidationException>())
            .WithMessage("*Monday wurde nicht vom Cube gelesen*");
        service.HasWeekProfile(1, DayOfWeek.Saturday).Should().BeTrue();
        service.HasWeekProfile(1, DayOfWeek.Monday).Should().BeFalse();
        gateway.LastProfile.Should().BeNull();
    }

    [Fact]
    public async Task Cached_state_can_be_loaded_for_offline_display()
    {
        var cube = BuildCube();
        var source = new FakeStore();
        await new CubeApplicationService(new FakeGateway { CubeToReturn = cube }, source, new FakeClock())
            .ConnectAsync(new CubeEndpoint("cube", 62910));

        var targetStore = new FakeStore { Settings = source.Settings, Snapshot = source.Snapshot };
        var service = new CubeApplicationService(new FakeGateway(), targetStore, new FakeClock());
        var restored = await service.LoadCachedAsync();

        restored.Should().NotBeNull();
        restored!.Rooms.Should().ContainSingle();
        restored.Rooms[0].Name.Should().Be("Wohnen");
        restored.Devices.Should().ContainSingle();
        restored.Devices[0].StateInfo.Should().Be(DeviceStateInfo.NotInitialized);
    }

    [Fact]
    public async Task Offline_cache_is_read_only()
    {
        var source = new FakeStore();
        await new CubeApplicationService(new FakeGateway { CubeToReturn = BuildCube() }, source, new FakeClock())
            .ConnectAsync(new CubeEndpoint("cube", 62910));
        var store = new FakeStore { Settings = source.Settings, Snapshot = source.Snapshot };
        var gateway = new FakeGateway();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        await service.LoadCachedAsync();

        service.IsOffline.Should().BeTrue();
        var act = () => service.SetRoomTemperatureAsync(1, new Temperature(22.0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        gateway.Calls.Should().NotContain(call => call.Contains("temperature"));
    }

    [Fact]
    public async Task Disconnect_clears_current_cube()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));

        await service.DisconnectAsync();

        service.Current.Should().BeNull();
        service.ConnectionState.Should().Be(CubeConnectionState.Disconnected);
    }

    [Fact]
    public async Task Disposing_the_service_detaches_from_gateway_events()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube() };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        gateway.RaiseSessionUnreachable();
        service.IsStale.Should().BeTrue();

        await service.DisposeAsync();

        gateway.RaiseCubeRefreshed(BuildCube());
        service.IsStale.Should().BeTrue("nach dem Dispose darf kein Gateway-Event mehr wirken");
    }

    [Fact]
    public async Task Disconnect_is_bounded_even_when_the_gateway_hangs()
    {
        var gateway = new FakeGateway { CubeToReturn = BuildCube(), HangOnDisconnect = true };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        string? warning = null;
        service.Warning += (_, text) => warning = text;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await service.DisconnectAsync();
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        service.Current.Should().BeNull();
        warning.Should().Contain("länger als erwartet");
    }

    [Fact]
    public async Task The_callers_cancellation_token_reaches_the_gateway()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway
        {
            CubeToReturn = BuildCube(),
            ConnectHook = async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
        };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        using var cts = new CancellationTokenSource();

        var connect = service.ConnectAsync(new CubeEndpoint("cube", 62910), cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        Func<Task> act = () => connect;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_live_refresh_is_cached_and_clears_the_stale_marker()
    {
        var cube = BuildCube();
        var gateway = new FakeGateway { CubeToReturn = cube };
        var store = new FakeStore();
        var service = new CubeApplicationService(gateway, store, new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        var savesAfterConnect = store.SaveCount;
        gateway.RaiseSessionUnreachable();
        service.IsStale.Should().BeTrue();

        gateway.RaiseCubeRefreshed(cube);

        service.IsStale.Should().BeFalse();
        store.SaveCount.Should().Be(savesAfterConnect + 1);
        await service.DisconnectAsync();
    }

    [Fact]
    public async Task An_unreachable_session_reconnects_with_backoff()
    {
        var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new FakeGateway
        {
            CubeToReturn = BuildCube(),
            ConnectHook = token =>
            {
                reconnected.TrySetResult();
                return Task.CompletedTask;
            },
        };
        var service = new CubeApplicationService(gateway, new FakeStore(), new FakeClock());
        await service.ConnectAsync(new CubeEndpoint("cube", 62910));
        reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        gateway.RaiseSessionUnreachable();

        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(4));
        service.IsStale.Should().BeFalse();
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeGateway : ICubeGateway
    {
        public Cube? CubeToReturn { get; set; }
        public Func<CancellationToken, Task>? ConnectHook { get; set; }
        public List<string> Calls { get; } = new();
        public Temperature? LastTemperature { get; private set; }
        public RoomControlMode? LastMode { get; private set; }
        public DayProfile? LastProfile { get; private set; }

        public CubeConnectionState State { get; private set; } = CubeConnectionState.Disconnected;

        public event EventHandler<CubeConnectionState>? StateChanged;

        public event EventHandler<Cube>? CubeRefreshed;

        public event EventHandler? SessionUnreachable;

        public void RaiseCubeRefreshed(Cube cube) => CubeRefreshed?.Invoke(this, cube);

        public void RaiseSessionUnreachable() => SessionUnreachable?.Invoke(this, EventArgs.Empty);

        public async Task<Cube> ConnectAsync(CubeEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            Calls.Add($"connect:{endpoint.Host}:{endpoint.Port}");
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
            Calls.Add($"temperature:{roomId}");
            LastTemperature = setPoint;
            return Task.CompletedTask;
        }

        public Task SetRoomControlModeAsync(
            int roomId, RadioAddress master, RoomControlMode mode, Temperature? setPoint,
            DateTimeOffset? until, CancellationToken cancellationToken = default)
        {
            Calls.Add($"mode:{roomId}:{mode}");
            LastMode = mode;
            LastTemperature = setPoint;
            return Task.CompletedTask;
        }

        public Task SetWeekDayProfileAsync(
            int roomId, RadioAddress master, DayProfile profile, CancellationToken cancellationToken = default)
        {
            Calls.Add($"profile:{roomId}:{profile.Day}");
            LastProfile = profile;
            return Task.CompletedTask;
        }

        /// <summary>Simuliert ein Gateway, das beim Trennen hängt und den Token ignoriert.</summary>
        public bool HangOnDisconnect { get; set; }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (HangOnDisconnect)
            {
                return Task.Delay(Timeout.Infinite);
            }

            State = CubeConnectionState.Disconnected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public void RaiseFaulted()
        {
            State = CubeConnectionState.Faulted;
            StateChanged?.Invoke(this, State);
        }
    }

    private sealed class FakeStore : ICubeStateStore
    {
        public Dictionary<string, string> Settings { get; set; } = new();
        public CubeSnapshot? Snapshot { get; set; }
        public List<string> Audit { get; } = new();
        public bool FailSave { get; set; }

        public int SaveCount { get; private set; }

        public Task<CubeSnapshot?> LoadAsync(string serialNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task SaveAsync(CubeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new InvalidOperationException("Simulated cache write failure.");
            }

            Snapshot = snapshot;
            SaveCount++;
            return Task.CompletedTask;
        }

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
