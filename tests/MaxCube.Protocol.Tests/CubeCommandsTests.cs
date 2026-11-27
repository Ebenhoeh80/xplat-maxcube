using FluentAssertions;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class CubeCommandsTests
{
    public static TheoryData<ICubeCommand, string> ExpectedWireLines => new()
    {
        { new ListDevicesCommand(), "l:" },
        { new GetConfigurationCommand(0x1D2A3C), "c:1d2a3c" },
        { new SendMetadataBlockCommand(0, System.Text.Encoding.UTF8.GetBytes("{\"rooms\":[]}")), "m:00,{\"rooms\":[]}" },
        { new StartInclusionCommand(60), "n:003c" },
        { new CancelInclusionCommand(), "x:" },
        { new DisconnectCommand(), "q:" },
        { new SetUrlCommand("http://www.eq3.de"), "u:http://www.eq3.de" },
        { new SetIntervalCommand(1000), "i:000003e8,00000000" },
        { new SendRadiogramCommand(new byte[] { 0xFB, 0x00, 0x01 }), "s:+wAB" },
        { new ResetDeviceCommand(0x1D2A3C), "r:01,HSo8" },
        { new EncryptCommand("something"), "e:something" },
        { new DecryptCommand("4b9m"), "d:4b9m" },
        { new ResetGatewayCommand(), "a:" },
        { new DeleteDevicesCommand(new[] { 0x1D2A3C }, force: true), "t:01,1,HSo8" },
        { new SetPushButtonConfigurationCommand(new byte[66]), "w:" + Convert.ToBase64String(new byte[66]) },
        { new SetTimezoneConfigurationCommand(new byte[24], 28), "v:" + Convert.ToBase64String(new byte[24]) + ",1c" },
        { new SetNtpServerCommand("pool.ntp.org", "time.eq-3.de"), "f:pool.ntp.org,time.eq-3.de" },
        { new QueryNtpServerCommand(), "f:" },
        { new WakeupCommand(WakeupContext.All, 0), "z:1e,A" },
        { new WakeupCommand(WakeupContext.Device, 0x1D2A3C), "z:1e,D,1d2a3c" },
        { new WakeupCommand(WakeupContext.Group, 5), "z:1e,G,05" },
    };

    [Theory]
    [MemberData(nameof(ExpectedWireLines))]
    public void Every_known_opcode_builds_the_documented_wire_line(ICubeCommand command, string expected)
        => command.WireLine.Should().Be(expected);

    [Fact]
    public void Wakeup_duration_covers_one_byte_and_rejects_larger_values()
    {
        new WakeupCommand(WakeupContext.Group, 5, 0xFF).WireLine.Should().Be("z:ff,G,05");
        var tooLarge = () => new WakeupCommand(WakeupContext.Group, 5, 256);
        tooLarge.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Wakeup_uses_the_acknowledgement_path()
    {
        var acknowledged = await ExecuteWithSingleResponseAsync(new WakeupCommand(WakeupContext.All, 0), "A:");
        acknowledged.Should().BeTrue();

        var rejected = async () => await ExecuteWithSingleResponseAsync(
            new WakeupCommand(WakeupContext.Group, 5), "r:01");
        await rejected.Should().ThrowAsync<CubeCommandRejectedException>();
    }

    [Fact]
    public async Task Acknowledge_is_success_but_r_and_unknown_prefixes_are_errors()
    {
        var ack = new AcknowledgedCommand("u:http://www.eq3.de");
        await using var transport = new ScriptedCubeTransport("A:", "r:01", "Z:what");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        var success = await queue.ExecuteAsync(ack);
        success.Should().BeTrue();
        await queue.Invoking(q => q.ExecuteAsync(new AcknowledgedCommand("u:again")))
            .Should().ThrowAsync<CubeCommandRejectedException>();
        queue.IsFaulted.Should().BeFalse();
        // The queue only rejects the bad command; check the other error paths in fresh queues.
        var unknown = async () => await ExecuteWithSingleResponseAsync(new AcknowledgedCommand("u:x"), "Z:what");
        await unknown.Should().ThrowAsync<UnknownCubeOpcodeException>();
    }

    [Fact]
    public async Task Radiogram_status_is_parsed_and_discarded_frames_are_rejected()
    {
        var good = await ExecuteWithSingleResponseAsync(new SendRadiogramCommand(new byte[] { 1 }), "S:05,0,1f");
        good.Should().Be(new RadioStatus(5, false, 31));

        var discarded = async () => await ExecuteWithSingleResponseAsync(
            new SendRadiogramCommand(new byte[] { 1 }), "S:64,1,00");
        (await discarded.Should().ThrowAsync<CubeCommunicationFailedException>())
            .WithMessage("*Command discarded*");
    }

    [Fact]
    public async Task Cancel_inclusion_reads_all_N_lines_until_the_empty_one()
    {
        var transport = new ScriptedCubeTransport("N:AAAA", "N:BBBB", "N:");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        (await queue.ExecuteAsync(new CancelInclusionCommand())).Should().Be(3);
    }

    [Fact]
    public async Task Cancel_inclusion_ignores_lines_without_the_N_prefix()
    {
        var transport = new ScriptedCubeTransport("X:ignore", "N:AAAA", "Y:ignore", "N:");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        (await queue.ExecuteAsync(new CancelInclusionCommand())).Should().Be(2);
    }

    [Fact]
    public async Task Start_inclusion_waits_longer_than_an_ordinary_command()
    {
        var command = new StartInclusionCommand(60);
        command.Timeout.Should().Be(TimeSpan.FromSeconds(70));
        command.ResolveInclusionModeEffect("N:AAAA")
            .Should().Be(InclusionModeEffect.Enter);
        command.ResolveInclusionModeEffect("N:")
            .Should().Be(InclusionModeEffect.Exit);

        // An empty N: body ends inclusion mode immediately (no 60 s wait).
        var transport = new ScriptedCubeTransport("N:");
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();

        (await queue.ExecuteAsync(command)).Should().Be(string.Empty);
        queue.IsInclusionMode.Should().BeFalse();
        queue.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public void Metadata_block_limits_are_enforced()
    {
        var tooLarge = () => new SendMetadataBlockCommand(0, new byte[1901]);
        var badIndex = () => new SendMetadataBlockCommand(100, Array.Empty<byte>());

        tooLarge.Should().Throw<ArgumentException>();
        badIndex.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Metadata_block_rejects_line_breaks_that_would_split_the_command()
    {
        var withCr = () => new SendMetadataBlockCommand(0, System.Text.Encoding.UTF8.GetBytes("a\rb"));
        var withLf = () => new SendMetadataBlockCommand(0, System.Text.Encoding.UTF8.GetBytes("a\nb"));

        withCr.Should().Throw<ArgumentException>().WithMessage("*CR or LF*");
        withLf.Should().Throw<ArgumentException>().WithMessage("*CR or LF*");
    }

    private static async Task<T> ExecuteWithSingleResponseAsync<T>(CubeCommand<T> command, string response)
    {
        await using var transport = new ScriptedCubeTransport(response);
        await using var queue = new CubeCommandQueue(transport, new CubeConnectionOptions());
        queue.Start();
        return await queue.ExecuteAsync(command);
    }
}
