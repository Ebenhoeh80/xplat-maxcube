using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Infrastructure.Tests;

public class SqliteStateStoreTests
{
    private static Cube BuildCube()
    {
        var cube = new Cube(new SerialNumber("JEQ0001234"), 275, new RadioAddress(0x1D2A3C), true);
        cube.AddRoom(new Room(1, "Wohnen", new RadioAddress(0x1D2A3C)));
        cube.AddDevice(new Device(
            new RadioAddress(0x0A0B0C), DeviceType.HeatingThermostat, 1,
            new SerialNumber("JEQ0001234"), "Thermostat", 1));
        cube.SetRoomTemperature(1, new Temperature(21.5));
        cube.ApplyRoomConfiguration(1,
            new Temperature(21.5), new Temperature(16.5), new Temperature(30.5), new Temperature(4.5),
            0.0, new Temperature(12.0), 15, 5, 80, DayOfWeek.Saturday, 12);
        return cube;
    }

    [Fact]
    public async Task Save_and_load_preserves_the_snapshot()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());
        var snapshot = CubeSnapshot.From(BuildCube(), new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync("JEQ0001234");

        loaded.Should().NotBeNull();
        loaded!.SerialNumber.Should().Be("JEQ0001234");
        loaded.FirmwareVersion.Should().Be(275);
        loaded.RfAddress.Should().Be("1D2A3C");
        loaded.DaylightSaving.Should().BeTrue();
        loaded.Rooms.Should().ContainSingle();
        loaded.Rooms[0].Name.Should().Be("Wohnen");
        loaded.Rooms[0].SetPoint.Should().Be(21.5);
        loaded.Rooms[0].Minimum.Should().Be(4.5);
        loaded.Rooms[0].BoostValve.Should().Be(80);
        loaded.Devices.Should().ContainSingle();
        loaded.Devices[0].Address.Should().Be("0A0B0C");
        loaded.Devices[0].StateInfo.Should().Be(DeviceStateInfo.NotInitialized);
    }

    [Fact]
    public async Task Save_overwrites_the_previous_cache_for_the_same_serial()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());
        var first = CubeSnapshot.From(BuildCube(), DateTimeOffset.UtcNow);
        await store.SaveAsync(first);

        var cube = BuildCube();
        cube.AddRoom(new Room(2, "Schlafen", new RadioAddress(0x1D2A3D)));
        var second = CubeSnapshot.From(cube, DateTimeOffset.UtcNow);
        await store.SaveAsync(second);

        var loaded = await store.LoadAsync("JEQ0001234");
        loaded!.Rooms.Should().HaveCount(2);
        loaded.Rooms.Should().Contain(room => room.Name == "Schlafen");
    }

    [Fact]
    public async Task Settings_roundtrip_and_unknown_keys_return_null()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());

        (await store.GetSettingAsync("cube.serial")).Should().BeNull();
        await store.SetSettingAsync("cube.serial", "JEQ0001234");
        await store.SetSettingAsync("cube.serial", "JEQ0009999");

        (await store.GetSettingAsync("cube.serial")).Should().Be("JEQ0009999");
    }

    [Fact]
    public async Task Audit_is_append_only()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());

        await store.AppendAuditAsync("Connected");
        await store.AppendAuditAsync("Temperatur 1=21,5");

        (await store.ReadAuditAsync()).Should().Equal("[user] Connected", "[user] Temperatur 1=21,5");
    }

    [Fact]
    public async Task Audit_entries_keep_their_source_tag()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());

        await store.AppendAuditAsync("verbinden", AuditSource.Session);
        await store.AppendAuditAsync("neu verbunden", AuditSource.Reconnect);
        await store.AppendAuditAsync("gesendet", AuditSource.User);
        await store.AppendAuditAsync("fehlgeschlagen", AuditSource.System);

        (await store.ReadAuditAsync()).Should().Equal(
            "[session] verbinden", "[reconnect] neu verbunden", "[user] gesendet", "[system] fehlgeschlagen");
    }

    [Fact]
    public async Task Audit_retention_deletes_only_entries_before_the_cutoff()
    {
        var clock = new FakeClock();
        await using var store = new SqliteStateStore("Data Source=:memory:", clock);
        await store.AppendAuditAsync("alt");
        clock.Now = clock.Now.AddDays(31);
        await store.AppendAuditAsync("neu");

        var removed = await store.DeleteAuditOlderThanAsync(clock.Now.AddDays(-30));

        removed.Should().Be(1);
        (await store.ReadAuditAsync()).Should().Equal("[user] neu");
    }

    [Fact]
    public async Task Snapshot_reconstructs_a_writable_cube()
    {
        await using var store = new SqliteStateStore("Data Source=:memory:", new FakeClock());
        await store.SaveAsync(CubeSnapshot.From(BuildCube(), DateTimeOffset.UtcNow));

        var cube = (await store.LoadAsync("JEQ0001234"))!.ToCube();

        cube.GetRoom(1).Should().NotBeNull();
        cube.GetRoom(1)!.SetPointTemperature.Should().Be(new Temperature(21.5));
        cube.GetRoom(1)!.DecalcificationDay.Should().Be(DayOfWeek.Saturday);
        cube.Devices.Should().ContainSingle();
    }

    [Fact]
    public async Task Schema_and_app_version_are_stored_idempotently()
    {
        var database = Path.Combine(Path.GetTempPath(), $"maxcube-version-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={database};Pooling=False";
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var store = new SqliteStateStore(
                    connectionString, new FakeClock(), new TestVersion(attempt == 0 ? "0.2.0" : "0.3.0"));
            }

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Convert.ToInt32(await version.ExecuteScalarAsync()).Should().Be(1);

            using var settings = connection.CreateCommand();
            settings.CommandText = "SELECT key, value FROM settings WHERE key IN ('app.version', 'schema.version') ORDER BY key";
            using var reader = await settings.ExecuteReaderAsync();
            var values = new Dictionary<string, string>();
            while (await reader.ReadAsync())
            {
                values[reader.GetString(0)] = reader.GetString(1);
            }

            values["app.version"].Should().Be("0.3.0");
            values["schema.version"].Should().Be("1");
        }
        finally
        {
            File.Delete(database);
        }
    }

    private sealed class TestVersion : IAppVersionProvider
    {
        public TestVersion(string version) => Version = version;

        public string Version { get; }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }
}
