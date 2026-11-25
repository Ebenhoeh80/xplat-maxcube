using FluentAssertions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.Handshake;
using Xunit;

namespace MaxCube.Protocol.Tests;

public class HelloParserTests
{
    // Datum/Zeit sind hexadezimale Bytes: 1a=26, 0b=11, 18=24; 0c=12, 1e=30
    // (MaxCubeStateFactory.java:147-156).
    private const string FullHello = "H:JEQ0001234,1d2a3c,111,00,00,05,1f,1a0b18,0c1e,3,0002";

    [Fact]
    public void Parses_full_hello_with_all_optional_fields()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero));
        var hello = HelloParser.Parse(FullHello, clock);

        hello.SerialNumber.Should().Be("JEQ0001234");
        hello.RadioAddress.Should().Be(0x1D2A3C);
        hello.FirmwareVersion.Should().Be(273);
        hello.DutyCycle.Should().Be(5);
        hello.FreeSlots.Should().Be(31);
        hello.CubeDate.Should().Be(new DateOnly(2026, 11, 24));
        hello.CubeTime.Should().Be(new TimeOnly(12, 30));
        hello.StateCubeTime.Should().Be(3);
        hello.NtpCounter.Should().Be(2);
        hello.RawBody.Should().Be(FullHello[2..]);
    }

    [Fact]
    public void Parses_minimal_hello_from_AP_1_1_acceptance_criteria()
    {
        var hello = HelloParser.Parse("H:001234567890,12345678,0111");

        hello.SerialNumber.Should().Be("001234567890");
        hello.RadioAddress.Should().Be(0x12345678);
        hello.FirmwareVersion.Should().Be(273);
        hello.DutyCycle.Should().BeNull();
        hello.FreeSlots.Should().BeNull();
        hello.CubeDate.Should().BeNull();
        hello.CubeTime.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("L:098EAB,0000")]
    public void Rejects_missing_or_wrong_prefix(string? line)
    {
        var act = () => HelloParser.Parse(line);

        act.Should().Throw<NoIncomingHelloException>().WithMessage("No Incoming Hello");
    }

    [Theory]
    [InlineData("H:JEQ0001234")]
    [InlineData("H:JEQ0001234,1d2a3c")]
    [InlineData("H:JEQ0001234,zzzzzz,111")]
    [InlineData("H:JEQ0001234,1d2a3c,zz")]
    [InlineData("H:,1d2a3c,111")]
    [InlineData("H:")]
    public void Rejects_malformed_mandatory_fields(string line)
    {
        var act = () => HelloParser.Parse(line);

        act.Should().Throw<MalformedHelloException>();
    }

    [Fact]
    public void Ignores_unparsable_optional_fields_instead_of_failing_the_handshake()
    {
        var hello = HelloParser.Parse("H:JEQ0001234,1d2a3c,111,??,zz,GG,xx,nope,bad,abc,zz");

        hello.FirmwareVersion.Should().Be(273);
        hello.DutyCycle.Should().BeNull();
        hello.CubeDate.Should().BeNull();
        hello.CubeTime.Should().BeNull();
    }
}
