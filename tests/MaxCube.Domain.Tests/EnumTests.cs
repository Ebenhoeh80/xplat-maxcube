using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Domain.Tests;

public class EnumTests
{
    [Theory]
    [InlineData(1, DeviceType.HeatingThermostat)]
    [InlineData(2, DeviceType.HeatingThermostatPlus)]
    [InlineData(3, DeviceType.WallMountedThermostat)]
    [InlineData(4, DeviceType.ShutterContact)]
    [InlineData(5, DeviceType.PushButton)]
    public void Device_type_codes_match_the_java_enum(int code, DeviceType expected)
    {
        code.FromValue().Should().Be(expected);
        ((int)expected).Should().Be(code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(256)]
    public void Unknown_device_type_codes_become_invalid(int code)
    {
        code.FromValue().Should().Be(DeviceType.Invalid);
    }

    [Fact]
    public void Invalid_has_the_belegten_sentinel_256()
    {
        ((int)DeviceType.Invalid).Should().Be(256);
    }

    [Fact]
    public void Mode_and_state_enums_are_exhaustive()
    {
        Enum.GetNames<RoomControlMode>().Should().Equal("Auto", "Permanently", "Temporary", "NotAvailable");
        Enum.GetNames<RoomTemperatureMode>().Should().Equal("Eco", "Comfort", "Normal", "NotAvailable");
        Enum.GetNames<DeviceStateInfo>().Should().Equal("Valid", "NotInitialized", "OutOfDate");
        Enum.GetNames<RadioState>().Should().Equal(
            "Ok", "Error", "ControlFailed", "ConfigFailed", "AssignFailed", "DeleteFailed");
        Enum.GetNames<PushButtonKeyMode>().Should().Equal(
            "Auto", "AutoWithTemperature", "Eco", "Comfort", "Manual");
    }

    [Fact]
    public void Day_of_week_is_one_based_and_unlike_system_day_of_week()
    {
        ((int)DayOfWeek.Monday).Should().Be(1);
        ((int)DayOfWeek.Saturday).Should().Be(6);
        ((int)DayOfWeek.Sunday).Should().Be(7);
        ((int)System.DayOfWeek.Sunday).Should().Be(0);

        DayOfWeekExtensions.FromMaxValue(1).Should().Be(DayOfWeek.Monday);
        DayOfWeekExtensions.FromMaxValue(7).Should().Be(DayOfWeek.Sunday);
        DayOfWeekExtensions.FromCalendarDay(1).Should().Be(DayOfWeek.Sunday);
        DayOfWeekExtensions.FromCalendarDay(2).Should().Be(DayOfWeek.Monday);
        DayOfWeekExtensions.FromCalendarDay(7).Should().Be(DayOfWeek.Saturday);
    }

    [Fact]
    public void Unknown_day_of_week_is_rejected()
    {
        var act = () => DayOfWeekExtensions.FromMaxValue(0);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be(DomainValidationCodes.Validation);
    }
}
