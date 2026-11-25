using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Tests;

/// <summary>
/// Wire-Zeilen aus dem kontrollierten Hardwarelauf am 25.11.2026 gegen 192.168.10.60.
/// Schreibformate wurden mit gelesenen Ursprungswerten oder Dummy-Adressen gesendet;
/// der Cube blieb danach im Ursprungszustand (docs/HARDWARE-VERIFY.md §7).
/// </summary>
public class HardwareGoldenCaptureTests
{
    [Fact]
    public void Valve_functions_wire_line_matches_hardware_capture()
    {
        var command = new SetRoomValveFunctionsCommand(
            RadioAddress.Parse("1AB088"), 1, 5, 80, DayOfWeek.Saturday, 12, 255, 0);

        command.WireLine.Should().Be("s:AAQSAAAAGrCIATAM/wA=");
    }

    [Theory]
    [InlineData(true, "s:AAAgAAAAAAAAAAAAAQE=")]
    [InlineData(false, "s:AAAhAAAAAAAAAAAAAQE=")]
    public void Link_partner_wire_lines_match_hardware_capture(bool add, string expected)
    {
        var command = new LinkPartnerCommand(
            RadioAddress.Parse("000000"), RadioAddress.Parse("000001"), 1, add);

        command.WireLine.Should().Be(expected);
    }

    [Fact]
    public void Group_header_link_wire_line_was_accepted_by_hardware()
    {
        var command = new LinkPartnerInRoomCommand(
            RadioAddress.Parse("1AB088"), 1, RadioAddress.Parse("000001"), 1, add: true);

        // Hardware 25.11.2026: S:42,0,30 (discarded=false, duty 66, free 48).
        command.WireLine.Should().Be("s:AAQgAAAAGrCIAQAAAQE=");
    }

    [Fact]
    public void Group_address_wire_lines_match_hardware_capture()
    {
        var dummy = RadioAddress.Parse("000000");

        new SetGroupAddressCommand(dummy, 1).WireLine.Should().Be("s:AAAiAAAAAAAAAAE=");
        new RemoveGroupAddressCommand(dummy, 1).WireLine.Should().Be("s:AAAjAAAAAAAAAAE=");
    }

    [Fact]
    public void Push_button_timezone_and_wakeup_wire_lines_match_hardware_capture()
    {
        // Die Nutzdaten stammen aus dem gelesenen C:-Block des Original-Cubes und wurden
        // byte-identisch zurückgeschrieben; daher sind die vollständigen Zeilen golden.
        new SetPushButtonConfigurationCommand(Convert.FromBase64String(
                "CwAEQAAAAAAAAAAA////////////////////////////CwAEQAAAAAAAAABB////////////////////////////"))
            .WireLine.Should().Be(
                "w:CwAEQAAAAAAAAAAA////////////////////////////CwAEQAAAAAAAAABB////////////////////////////");

        new SetTimezoneConfigurationCommand(Convert.FromBase64String("Q0VUAAAKAAMAAA4QQ0VTVAADAAIAABwg"))
            .WireLine.Should().Be("v:Q0VUAAAKAAMAAA4QQ0VTVAADAAIAABwg");

        new WakeupCommand(WakeupContext.All, 0, 0).WireLine.Should().Be("z:00,A");
    }

    [Theory]
    [InlineData("S:36,0,30", 54, false, 48)]
    [InlineData("S:36,1,30", 54, true, 48)]
    [InlineData("S:3c,1,31", 60, true, 49)]
    [InlineData("S:3a,0,31", 58, false, 49)]
    [InlineData("S:3c,0,30", 60, false, 48)]
    public async Task Hardware_status_lines_decode_to_discarded_flag_or_airtime(
        string response, int duty, bool discarded, int freeSlots)
    {
        if (discarded)
        {
            var rejected = async () => await ExecuteWithSingleResponseAsync(
                new SendRadiogramCommand(new byte[] { 1 }), response);
            (await rejected.Should().ThrowAsync<CubeCommunicationFailedException>())
                .WithMessage("*Command discarded*");
            return;
        }

        var status = await ExecuteWithSingleResponseAsync(new SendRadiogramCommand(new byte[] { 1 }), response);
        status.Should().Be(new RadioStatus(duty, false, freeSlots));
    }

    private static async Task<T> ExecuteWithSingleResponseAsync<T>(CubeCommand<T> command, string response)
    {
        await using var queue = new CubeCommandQueue(
            new ScriptedCubeTransport(response), new CubeConnectionOptions());
        queue.Start();
        return await queue.ExecuteAsync(command);
    }
}
