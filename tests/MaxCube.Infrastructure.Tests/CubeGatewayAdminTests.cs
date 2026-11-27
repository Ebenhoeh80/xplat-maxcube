using System.Text;
using System.Threading.Channels;
using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Infrastructure.Connectivity;
using MaxCube.Protocol;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

/// <summary>
/// Gremium Runde 5: Die erweiterten Gateway-Funktionen laufen durch die echte Queue und den
/// echten Worker gegen einen reaktiven Fake-Cube (kein Netzwerk, keine Hardware).
/// </summary>
public class CubeGatewayAdminTests
{
    private static readonly RadioAddress CubeAddress = new(0x1D2A3C);

    private static byte[] Metadata(bool invalidRoomName = false)
    {
        var bytes = new List<byte> { MetadataDecoder.MagicByte, MetadataDecoder.ActualVersion, 1, 1, 6 };
        bytes.AddRange(Encoding.UTF8.GetBytes("Wohnen"));
        if (invalidRoomName)
        {
            bytes[5] = 0xFF;
        }

        bytes.AddRange([0x1D, 0x2A, 0x3C]);
        bytes.Add(1);
        bytes.Add(1);
        bytes.AddRange([0x0A, 0x0B, 0x0C]);
        bytes.AddRange(Encoding.ASCII.GetBytes("JEQ0001234"));
        bytes.Add(10);
        bytes.AddRange(Encoding.UTF8.GetBytes("Thermostat"));
        bytes.Add(1);
        bytes.Add(1);
        return bytes.ToArray();
    }

    private static byte[] CubeConfiguration()
    {
        var bytes = new byte[CubeConfigurationCodec.MinimumLength + 4];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        // Taster oben/unten auf Auto; Zeitzone Winter/Sommer aus dem Preset.
        bytes[CubeConfigurationCodec.PushButtonOffsetUpper + CubeConfigurationCodec.PushButtonKeyByte] = 0;
        bytes[CubeConfigurationCodec.PushButtonOffsetLower + CubeConfigurationCodec.PushButtonKeyByte] = 0;
        var zone = CubeConfigurationCodec.BuildTimeZonePayload(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);
        Array.Copy(zone, 0, bytes, CubeConfigurationCodec.TimeZoneOffsetWinter, zone.Length);
        return bytes;
    }

    private static async Task<(CubeGateway Gateway, ReactiveCube Cube, MaxCube.Domain.Aggregates.Cube Aggregate)> ConnectAsync(
        bool invalidRoomName = false)
    {
        var cube = new ReactiveCube(Metadata(invalidRoomName), CubeConfiguration());
        var gateway = new CubeGateway(transportFactory: (_, _) => cube);
        var aggregate = await gateway.ConnectAsync(new CubeEndpoint("fake", 62910));
        return (gateway, cube, aggregate);
    }

    [Fact]
    public async Task Metadata_is_written_as_numbered_blocks_after_the_safety_check()
    {
        var (gateway, cube, aggregate) = await ConnectAsync();
        await using var _ = gateway;
        aggregate.RenameRoom(1, "Büro");

        await gateway.WriteMetadataAsync(aggregate);

        var line = cube.Sent.Should().ContainSingle(sent => sent.StartsWith("m:", StringComparison.Ordinal)).Subject;
        line.Should().StartWith("m:00,");
        var decoded = MetadataDecoder.Decode(line[5..]);
        decoded.Rooms.Should().ContainSingle().Which.Name.Should().Be("Büro");
        decoded.Devices.Should().ContainSingle().Which.Name.Should().Be("Thermostat");
        decoded.Version.Should().Be(MetadataDecoder.ActualVersion);
    }

    [Fact]
    public async Task Metadata_is_refused_before_the_first_byte_when_the_round_trip_fails()
    {
        var (gateway, cube, aggregate) = await ConnectAsync(invalidRoomName: true);
        await using var _ = gateway;

        var act = () => gateway.WriteMetadataAsync(aggregate);

        await act.Should().ThrowAsync<MalformedMetadataException>().WithMessage("*Nothing was sent*");
        cube.Sent.Should().NotContain(sent => sent.StartsWith("m:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_radiogram_reports_the_airtime_from_the_s_answer()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;
        AirtimeStatus? seen = null;
        gateway.AirtimeChanged += (_, status) => seen = status;

        await gateway.SetDisplayModeAsync(new RadioAddress(0x0A0B0D), showActualTemperature: true);

        seen.Should().Be(new AirtimeStatus(9, 47));
        cube.Sent.Should().Contain(line => line.StartsWith("s:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_returns_the_found_device_and_ends_the_inclusion_mode()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;
        var payload = new List<byte> { 4, 0x0A, 0x0B, 0x0E };
        payload.AddRange(Encoding.ASCII.GetBytes("JEQ0000009"));
        cube.NextInclusionAnswer = "N:" + Convert.ToBase64String(payload.ToArray());

        var found = await gateway.SearchNewDeviceAsync(TimeSpan.FromSeconds(10));

        found.Should().NotBeNull();
        found!.Type.Should().Be(DeviceType.ShutterContact);
        found.Address.Should().Be(new RadioAddress(0x0A0B0E));
        found.SerialNumber.Should().Be("JEQ0000009");
        cube.Sent.Should().ContainInOrder("n:000a", "x:");

        // Der Cube nimmt danach wieder normale Befehle an.
        var act = () => gateway.SetDisplayModeAsync(new RadioAddress(0x0A0B0D), false);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Search_without_a_device_ends_without_x()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;
        cube.NextInclusionAnswer = "N:";

        var found = await gateway.SearchNewDeviceAsync(TimeSpan.FromSeconds(5));

        found.Should().BeNull();
        cube.Sent.Should().Contain("n:0005").And.NotContain("x:");
    }

    [Fact]
    public async Task Push_button_is_read_and_written_with_66_template_bytes()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;

        var read = await gateway.ReadPushButtonAsync(CubeAddress);
        await gateway.SetPushButtonAsync(CubeAddress, new PushButtonConfiguration(
            new PushButtonKeyConfiguration(PushButtonKeyMode.Manual, new Temperature(22.0)),
            new PushButtonKeyConfiguration(PushButtonKeyMode.Comfort, null)));

        read.Upper.Mode.Should().Be(PushButtonKeyMode.Auto);
        cube.Sent.Should().Contain("c:1d2a3c");
        var line = cube.Sent.Single(sent => sent.StartsWith("w:", StringComparison.Ordinal));
        var payload = Convert.FromBase64String(line[2..]);
        payload.Should().HaveCount(66);
        payload[11].Should().Be((byte)((1 << 6) | 44));
        payload[33 + 11].Should().Be((byte)((1 << 6) | 2));
    }

    [Fact]
    public async Task Time_zone_and_cube_time_use_the_v_command()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;

        var zone = await gateway.ReadTimeZoneAsync(CubeAddress);
        await gateway.SetTimeZoneAsync(CubeAddress, zone, null);
        await gateway.SetTimeZoneAsync(CubeAddress, zone, new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

        zone.Should().Be(TimeZonePresets.All["Mitteleuropa (CET/CEST)"]);
        var lines = cube.Sent.Where(sent => sent.StartsWith("v:", StringComparison.Ordinal)).ToArray();
        lines.Should().HaveCount(2);
        lines[0].Should().NotContain(",");
        var seconds = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        lines[1].Should().EndWith("," + seconds.ToString("x"));
    }

    [Fact]
    public async Task Ntp_devices_and_delete_use_their_documented_wire_lines()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;

        var query = await gateway.QueryNtpServersAsync();
        await gateway.SetNtpServersAsync(["pool.ntp.org", "time.eq-3.de"]);
        await gateway.DeleteDevicesAsync([new RadioAddress(0x1D2A3C)], factoryReset: true);

        query.Should().Be("pool.ntp.org");
        cube.Sent.Should().Contain("f:");
        cube.Sent.Should().Contain("f:pool.ntp.org,time.eq-3.de");
        cube.Sent.Should().Contain("t:01,1," + Convert.ToBase64String([0x1D, 0x2A, 0x3C]));
    }

    [Fact]
    public async Task Wake_up_uses_the_documented_z_command_for_rooms_and_devices()
    {
        var (gateway, cube, _) = await ConnectAsync();
        await using var _ = gateway;

        await gateway.WakeUpRoomAsync(5, 45);
        await gateway.WakeUpDeviceAsync(new RadioAddress(0x1D2A3C));

        cube.Sent.Should().Contain("z:2d,G,05");
        cube.Sent.Should().Contain("z:1e,D,1d2a3c");
    }

    private sealed class ReactiveCube : ICubeTransport
    {
        private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
        private readonly byte[] _configuration;

        public ReactiveCube(byte[] metadata, byte[] configuration)
        {
            _configuration = configuration;
            _incoming.Writer.TryWrite("H:JEQ0001234,1d2a3c,0113");
            _incoming.Writer.TryWrite("M:00,01," + Convert.ToBase64String(metadata));
            _incoming.Writer.TryWrite("L:");
        }

        public List<string> Sent { get; } = new();

        public string NextInclusionAnswer { get; set; } = "N:";

        public bool IsConnected { get; private set; }

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
            => await _incoming.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            lock (Sent)
            {
                Sent.Add(line);
            }

            var answer = line switch
            {
                _ when line.StartsWith("m:", StringComparison.Ordinal) => "A:",
                _ when line.StartsWith("s:", StringComparison.Ordinal) => "S:09,0,2f",
                _ when line.StartsWith("c:", StringComparison.Ordinal) => "C:1d2a3c," + Convert.ToBase64String(_configuration),
                _ when line.StartsWith("v:", StringComparison.Ordinal) => "A:",
                _ when line.StartsWith("z:", StringComparison.Ordinal) => "A:",
                _ when line.StartsWith("w:", StringComparison.Ordinal) => "A:",
                _ when line.StartsWith("t:", StringComparison.Ordinal) => "A:",
                _ when line == "f:" => "F:pool.ntp.org",
                _ when line.StartsWith("f:", StringComparison.Ordinal) => "F:" + line[2..],
                _ when line.StartsWith("n:", StringComparison.Ordinal) => NextInclusionAnswer,
                "x:" => "N:",
                "q:" => null,
                _ => null,
            };

            if (answer is not null)
            {
                _incoming.Writer.TryWrite(answer);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
