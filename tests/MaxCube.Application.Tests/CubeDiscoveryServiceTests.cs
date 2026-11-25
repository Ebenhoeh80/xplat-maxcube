using System.Text;
using FluentAssertions;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;
using Xunit;

namespace MaxCube.Application.Tests;

public class CubeDiscoveryServiceTests
{
    [Fact]
    public void Request_matches_the_decompiled_java_literal()
    {
        var request = CubeDiscoveryProtocol.BuildRequest();

        request.Should().HaveCount(19);
        Encoding.ASCII.GetString(request).Should().Be("eQ3Max*\0**********I");
        request[7].Should().Be(0);
        request[18].Should().Be((byte)'I');
        CubeDiscoveryProtocol.UdpPort.Should().Be(23272);
    }

    [Fact]
    public void Response_decodes_serial_firmware_and_bootloader_flag()
    {
        var datagram = new DiscoveryDatagram(BuildResponse("JEQ0001234", 275), "192.168.178.3");

        var cube = CubeDiscoveryProtocol.TryParse(datagram);

        cube.Should().Be(new DiscoveredCube("JEQ0001234", 275, false, "192.168.178.3"));
    }

    [Fact]
    public void Malformed_responses_are_discarded()
    {
        CubeDiscoveryProtocol.TryParse(new DiscoveryDatagram(new byte[8], "192.168.178.3"))
            .Should().BeNull();
        CubeDiscoveryProtocol.TryParse(new DiscoveryDatagram(
                BuildResponse("JEQ0001234", 275), string.Empty))
            .Should().BeNull();
        var wrongSignature = BuildResponse("JEQ0001234", 275);
        wrongSignature[0] = (byte)'X';
        CubeDiscoveryProtocol.TryParse(new DiscoveryDatagram(wrongSignature, "192.168.178.3"))
            .Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_answers_keep_the_regular_cube_over_the_bootloader()
    {
        var transport = new FakeTransport(
            new DiscoveryDatagram(BuildResponse("JEQ0001234", 275, bootloader: true), "192.168.178.3"),
            new DiscoveryDatagram(BuildResponse("JEQ0001234", 275), "192.168.178.3"),
            new DiscoveryDatagram(BuildResponse("JEQ0005678", 261), "192.168.178.4"));
        var service = new CubeDiscoveryService(transport);

        var cubes = await service.DiscoverAsync(TimeSpan.FromMilliseconds(1));

        cubes.Should().HaveCount(2);
        cubes[0].Should().Be(new DiscoveredCube("JEQ0001234", 275, false, "192.168.178.3"));
        cubes[1].Should().Be(new DiscoveredCube("JEQ0005678", 261, false, "192.168.178.4"));
        transport.Request.Should().Equal(CubeDiscoveryProtocol.BuildRequest());
    }

    private static byte[] BuildResponse(string serial, int firmware, bool bootloader = false)
    {
        var bytes = new byte[26];
        Encoding.ASCII.GetBytes(bootloader ? "eQ3MaxBl" : "eQ3MaxAp").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(serial.PadRight(10)[..10]).CopyTo(bytes, 8);
        bytes[24] = (byte)(firmware >> 8);
        bytes[25] = (byte)firmware;
        return bytes;
    }

    private sealed class FakeTransport : IDiscoveryTransport
    {
        private readonly DiscoveryDatagram[] _datagrams;

        public FakeTransport(params DiscoveryDatagram[] datagrams) => _datagrams = datagrams;

        public byte[]? Request { get; private set; }

        public Task<IReadOnlyList<DiscoveryDatagram>> ProbeAsync(
            byte[] request, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<IReadOnlyList<DiscoveryDatagram>>(_datagrams);
        }
    }
}
