using System.Text;
using FluentAssertions;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;
using Xunit;

namespace MaxCube.Domain.Tests;

public class TemperatureTests
{
    [Fact]
    public void Constants_and_defaults_match_the_java_class()
    {
        Temperature.Default.Value.Should().Be(21.0);
        Temperature.Default.IsValid.Should().BeTrue();
        Temperature.Off.Value.Should().Be(4.5);
        Temperature.Off.IsOff.Should().BeTrue();
        Temperature.On.Value.Should().Be(30.5);
        Temperature.On.IsOn.Should().BeTrue();
    }

    [Theory]
    [InlineData(4.5)]
    [InlineData(30.5)]
    public void Measured_temperatures_have_no_off_or_on_normalisation(double input)
    {
        var measured = new MeasuredTemperature(input);

        measured.Value.Should().Be(input);
        measured.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_missing_measurement_is_zero_and_wire_values_are_rejected()
    {
        new MeasuredTemperature(0.0).IsValid.Should().BeFalse();
        MeasuredTemperature.FromWireTenths(456).IsValid.Should().BeFalse(); // kein erfundener 40,0-Wert
        MeasuredTemperature.FromWireTenths(200).Value.Should().Be(20.0);
        var act = () => new MeasuredTemperature(40.1);

        act.Should().Throw<DomainValidationException>();
    }

    [Theory]
    [InlineData(4.4)]
    [InlineData(4.5)]
    [InlineData(0.0)]
    public void Values_at_or_below_off_become_off(double input)
    {
        var temperature = new Temperature(input);

        temperature.Value.Should().Be(4.5);
        temperature.IsOff.Should().BeTrue();
        temperature.IsOn.Should().BeFalse();
    }

    [Theory]
    [InlineData(30.5)]
    [InlineData(30.6)]
    [InlineData(40.0)]
    public void Values_at_or_above_on_become_on(double input)
    {
        var temperature = new Temperature(input);

        temperature.Value.Should().Be(30.5);
        temperature.IsOn.Should().BeTrue();
        temperature.IsOff.Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(40.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Values_outside_the_validated_range_are_rejected(double input)
    {
        var act = () => new Temperature(input);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.Validation);
    }

    [Theory]
    [InlineData(4.6)]
    [InlineData(30.4)]
    public void Values_inside_the_range_are_kept(double input)
    {
        new Temperature(input).Value.Should().Be(input);
    }

    [Fact]
    public void FromWire_divides_by_two()
    {
        Temperature.FromWire(43).Value.Should().Be(21.5);
        Temperature.FromWire(9).Should().Be(Temperature.Off);
        Temperature.FromWire(61).Should().Be(Temperature.On);
    }

    [Fact]
    public void FromWire_normalizes_out_of_range_wire_values_like_java()
    {
        Temperature.FromWire(100).Should().Be(Temperature.On);
        Temperature.FromWire(-2).Should().Be(Temperature.Off);
    }

    [Fact]
    public void ToWire_roundtrips_the_belegte_wire_values()
    {
        new Temperature(21.5).ToWire().Should().Be(43);
        Temperature.Off.ToWire().Should().Be(9);
        Temperature.On.ToWire().Should().Be(61);
    }

    [Fact]
    public void Value_equality_and_formatting()
    {
        new Temperature(21.5).Should().Be(new Temperature(21.5));
        new Temperature(21.5).Should().NotBe(new Temperature(21.0));
        new Temperature(21.5).ToString().Should().Be("21.5°C");
    }
}

public class RadioAddressTests
{
    [Fact]
    public void Parse_accepts_six_hex_digits()
    {
        var address = RadioAddress.Parse("1d2a3c");

        address.Value.Should().Be(0x1D2A3C);
        address.ToString().Should().Be("1D2A3C");
        RadioAddress.Parse("1D2A3C").Should().Be(address);
    }

    [Theory]
    [InlineData("1d2a3")]
    [InlineData("1d2a3c7")]
    [InlineData(null)]
    public void Wrong_length_is_max1034(string? input)
    {
        var act = () => RadioAddress.Parse(input!);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.RadioAddressHasInvalidLength);
        RadioAddress.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Non_hex_digits_are_max1035()
    {
        var act = () => RadioAddress.Parse("zzzzzz");

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.RadioAddressIsNotHex);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x1000000)]
    public void Out_of_range_values_are_max1022(int value)
    {
        var act = () => new RadioAddress(value);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.InvalidDeviceRadioAddress);
    }

    [Fact]
    public void Bytes_roundtrip_big_endian()
    {
        var address = RadioAddress.FromBytes(0x1d, 0x2a, 0x3c);

        address.Value.Should().Be(0x1D2A3C);
        address.ToBytes().Should().Equal(0x1d, 0x2a, 0x3c);
    }
}

public class SerialNumberTests
{
    [Fact]
    public void Ten_characters_are_kept_verbatim()
    {
        var serial = new SerialNumber("JEQ0001234");

        serial.Value.Should().Be("JEQ0001234");
        serial.ToString().Should().Be("JEQ0001234");
        serial.ToWireBytes().Should().Equal(Encoding.ASCII.GetBytes("JEQ0001234"));
    }

    [Fact]
    public void Shorter_values_are_padded_with_spaces_on_the_wire()
    {
        var bytes = new SerialNumber("ABC").ToWireBytes();

        bytes.Should().HaveCount(10);
        bytes[..3].Should().Equal(Encoding.ASCII.GetBytes("ABC"));
        bytes[3..].Should().OnlyContain(b => b == (byte)' ');
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData(null)]
    public void Too_long_or_null_values_are_max1040(string? input)
    {
        var act = () => new SerialNumber(input!);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.SerialNumberInvalidLength);
    }

    [Fact]
    public void Default_serial_number_is_rejected_on_wire_conversion()
    {
        var act = () => default(SerialNumber).ToWireBytes();

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.SerialNumberInvalidLength);
    }

    [Fact]
    public void Value_equality()
    {
        new SerialNumber("JEQ0001234").Should().Be(new SerialNumber("JEQ0001234"));
        new SerialNumber("JEQ0001234").Should().NotBe(new SerialNumber("JEQ0001235"));
    }
}
