using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using MaxCube.Domain.Configuration;
using MaxCube.Infrastructure.Connectivity;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;
using Xunit;

namespace MaxCube.Infrastructure.Tests;

/// <summary>Gremium Runde 5: UDP-Netzwerkkonfiguration gegen einen Loopback-Fake-Cube.</summary>
public class UdpNetworkConfiguratorTests
{
    private static byte[] Response(byte opcode, byte[] data, string serial = "JEQ0001234")
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("eQ3Max*\0"));
        bytes.AddRange(Encoding.ASCII.GetBytes(serial.PadRight(10, '*')));
        bytes.Add((byte)'>');
        bytes.Add(opcode);
        bytes.AddRange(data);
        return bytes.ToArray();
    }

    private static byte[] NetworkData(bool dhcp, byte encryption = 0)
    {
        var data = new List<byte>();
        // Java-Reihenfolge: IP, Gateway, Maske, DNS1, DNS2 (CubeUdpRequestFactory.java:45-47).
        foreach (var address in new[] { "192.168.178.3", "192.168.178.1", "255.255.255.0", "192.168.178.1", "8.8.8.8" })
        {
            data.AddRange(IPAddress.Parse(address).GetAddressBytes());
        }

        data.Add(dhcp ? (byte)1 : (byte)0);
        data.Add(encryption);
        return data.ToArray();
    }

    [Fact]
    public void The_request_has_signature_serial_opcode_and_payload()
    {
        var request = CubeUdpCodec.BuildRequest("JEQ0001234", CubeUdpCodec.GetNetworkConfiguration);

        request.Should().HaveCount(19);
        Encoding.ASCII.GetString(request, 0, 7).Should().Be("eQ3Max*");
        request[7].Should().Be(0);
        Encoding.ASCII.GetString(request, 8, 10).Should().Be("JEQ0001234");
        request[18].Should().Be((byte)'c');
        CubeUdpCodec.BuildRequest("JEQ1", (byte)'C', [1, 2])[8..18].Should().Equal(Encoding.ASCII.GetBytes("JEQ1******"));
    }

    [Fact]
    public void The_reboot_request_uses_the_manufacturer_opcode_without_payload()
    {
        var request = CubeUdpCodec.BuildRequest("JEQ0001234", CubeUdpCodec.Reboot);

        request.Should().HaveCount(19);
        request[18].Should().Be((byte)'R');
        Encoding.ASCII.GetString(request, 8, 10).Should().Be("JEQ0001234");
    }

    [Fact]
    public void The_network_block_round_trips_and_keeps_unknown_trailing_bytes()
    {
        var data = NetworkData(dhcp: false, encryption: 7);

        var read = CubeUdpCodec.DecodeNetwork(data);
        var written = CubeUdpCodec.EncodeNetwork(read with { Dhcp = true }, data);

        read.Should().Be(new CubeNetworkConfiguration(
            "192.168.178.3", "255.255.255.0", "192.168.178.1", "192.168.178.1", "8.8.8.8", false));
        written.Should().HaveCount(22);
        written[20].Should().Be(1);
        written[21].Should().Be(7, "das Verschlüsselungs-Flag wird unverändert zurückgeschrieben");
        written[..20].Should().Equal(data[..20]);
    }

    [Fact]
    public void Hardware_capture_decodes_gateway_before_subnet_mask()
    {
        // FAKT (Hardware 25.11.2026): rohe c-Antwort von 192.168.10.60,
        // 42 Byte, Seriennummer JEQ0355273, Opcode 'c'.
        var datagram = Convert.FromHexString(
            "6551334D617841704A4551303335353237333E63C0A800DEC0A80001FFFF0000C0A80001000000000101");
        var payload = CubeUdpCodec.ParseResponse(datagram, "JEQ0355273", CubeUdpCodec.GetNetworkConfiguration);

        var configuration = CubeUdpCodec.DecodeNetwork(payload);

        configuration.Should().Be(new CubeNetworkConfiguration(
            "192.168.0.222", "255.255.0.0", "192.168.0.1", "192.168.0.1", "0.0.0.0", true));
        CubeUdpCodec.EncodeNetwork(configuration, payload).Should().Equal(payload);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("256.1.1.1")]
    [InlineData("a.b.c.d")]
    [InlineData("")]
    public void Invalid_addresses_are_refused_when_encoding(string address)
    {
        var configuration = new CubeNetworkConfiguration(address, "255.255.255.0", "1.2.3.1", "1.2.3.1", "8.8.8.8", false);

        var act = () => CubeUdpCodec.EncodeNetwork(configuration, NetworkData(false));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Foreign_or_malformed_responses_are_rejected()
    {
        FluentActions.Invoking(() => CubeUdpCodec.ParseResponse(new byte[5], "JEQ0001234", (byte)'c'))
            .Should().Throw<CubeCommunicationFailedException>();
        FluentActions.Invoking(() => CubeUdpCodec.ParseResponse(Response((byte)'h', []), "JEQ0001234", (byte)'c'))
            .Should().Throw<CubeCommunicationFailedException>();
        FluentActions.Invoking(() => CubeUdpCodec.ParseResponse(Response((byte)'c', [], "JEQ9999999"), "JEQ0001234", (byte)'c'))
            .Should().Throw<CubeCommunicationFailedException>().WithMessage("*different cube*");
        FluentActions.Invoking(() => CubeUdpCodec.DecodeNetwork(new byte[10]))
            .Should().Throw<CubeCommunicationFailedException>();
    }

    [Fact]
    public async Task Read_and_write_talk_to_the_cube_over_udp_unicast()
    {
        using var fakeCube = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)fakeCube.Client.LocalEndPoint!).Port;
        var received = new List<byte[]>();
        var server = Task.Run(async () =>
        {
            while (received.Count < 3)
            {
                var request = await fakeCube.ReceiveAsync();
                received.Add(request.Buffer);
                var opcode = request.Buffer[18];
                var answer = opcode == (byte)'c' ? Response((byte)'c', NetworkData(false, 7)) : Response((byte)'C', []);
                await fakeCube.SendAsync(answer, answer.Length, request.RemoteEndPoint);
            }
        });
        var configurator = new UdpNetworkConfigurator(port);

        var read = await configurator.ReadAsync("JEQ0001234", "127.0.0.1");
        await configurator.WriteAsync("JEQ0001234", "127.0.0.1", read with { Dhcp = true, IpAddress = "192.168.178.9" });
        await server.WaitAsync(TimeSpan.FromSeconds(5));

        read.IpAddress.Should().Be("192.168.178.3");
        received.Select(packet => (char)packet[18]).Should().Equal('c', 'c', 'C');
        var write = received[2];
        write[19..23].Should().Equal(192, 168, 178, 9);
        write[19 + 20].Should().Be(1);
        write[19 + 21].Should().Be(7);
    }

    [Fact]
    public async Task A_silent_cube_ends_in_a_clear_error()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
        var configurator = new UdpNetworkConfigurator(port);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = () => configurator.ReadAsync("JEQ0001234", "127.0.0.1", cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Only_ipv4_addresses_are_accepted_for_the_target()
    {
        var configurator = new UdpNetworkConfigurator();

        var act = () => configurator.ReadAsync("JEQ0001234", "not-an-ip");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Parallel_reads_serialize_on_the_fixed_local_udp_port()
    {
        using var fakeCube = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)fakeCube.Client.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            for (var requestNumber = 0; requestNumber < 2; requestNumber++)
            {
                var request = await fakeCube.ReceiveAsync(timeout.Token);
                var answer = Response((byte)'c', NetworkData(false));
                await fakeCube.SendAsync(answer, answer.Length, request.RemoteEndPoint);
            }
        }, timeout.Token);

        // Vor dem Port-Gate bindet die zweite Instanz denselben Port 23272 und endet
        // sofort in einer rohen SocketException (Kundenlog 27.11., 11:48:17).
        var results = await Task.WhenAll(
            new UdpNetworkConfigurator(port).ReadAsync("JEQ0001234", "127.0.0.1", timeout.Token),
            new UdpNetworkConfigurator(port).ReadAsync("JEQ0001234", "127.0.0.1", timeout.Token));

        await server;
        results.Should().HaveCount(2)
            .And.OnlyContain(configuration => configuration.IpAddress == "192.168.178.3");
    }

    [Fact]
    public async Task Socket_errors_end_in_a_cube_communication_exception()
    {
        var configurator = new UdpNetworkConfigurator();
        // Der feste lokale Port ist bereits belegt (z. B. durch einen parallelen Auto-Read).
        using var blocker = new UdpClient(new IPEndPoint(IPAddress.Any, CubeUdpCodec.Port));

        var act = () => configurator.ReadAsync("JEQ0001234", "127.0.0.1");

        var exception = await act.Should().ThrowAsync<CubeCommunicationFailedException>();
        exception.Which.Message.Should().Contain(CubeUdpCodec.Port.ToString());
        exception.Which.InnerException.Should().BeOfType<SocketException>();
    }
}
