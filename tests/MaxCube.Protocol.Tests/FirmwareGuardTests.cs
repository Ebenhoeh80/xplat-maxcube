using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class FirmwareGuardTests
{
    private readonly CubeConnectionOptions _options = new();

    [Theory]
    [InlineData(273)]
    [InlineData(274)]
    [InlineData(275)]
    public void Accepts_supported_firmware(int firmware)
    {
        var act = () => FirmwareGuard.EnsureSupported(firmware, _options);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(272)]
    [InlineData(276)]
    public void Rejects_firmware_outside_the_window(int firmware)
    {
        var act = () => FirmwareGuard.EnsureSupported(firmware, _options);

        act.Should().Throw<IncompatibleFirmwareException>()
            .WithMessage($"Firmware {firmware} is outside the supported range 273..275.");
    }

    [Fact]
    public void Honours_custom_bounds()
    {
        var options = _options with { MinimumFirmwareVersion = 300, MaximumFirmwareVersion = 310 };

        var act = () => FirmwareGuard.EnsureSupported(276, options);

        act.Should().Throw<IncompatibleFirmwareException>();
    }
}
