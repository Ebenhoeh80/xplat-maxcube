using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Domain.Schedules;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Radiograms;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Tests;

public class RoomCommandsTests
{
    private static readonly RadioAddress Master = new(0x1D2A3C);

    private static string Wire(string hex) => "s:" + Convert.ToBase64String(Convert.FromHexString(hex));

    [Fact]
    public void Auto_mode_matches_the_java_group_header_and_control_bits()
    {
        var command = new SetRoomAutoModeCommand(Master, 1);

        // 00 04 40 | 000000 | 1d2a3c | 01 | Modus 0, Temperatur 0
        command.WireLine.Should().Be(Wire("0004400000001d2a3c0100000000000000"));
    }

    [Fact]
    public void Permanent_mode_encodes_mode_and_setpoint_in_byte_10()
    {
        var command = new SetRoomPermanentModeCommand(Master, 1, new Temperature(21.0));

        // 0x2A = 21,0 °C * 2; 0x40 = Permanently (Byte 10 Bit 6)
        command.WireLine.Should().Be(Wire("0004400000001d2a3c016a000000000000"));
    }

    [Fact]
    public void Temporary_mode_uses_the_java_date_bit_positions()
    {
        var command = new SetRoomTemporaryModeCommand(
            Master, 1, new Temperature(19.5), new DateTimeOffset(2026, 12, 24, 18, 30, 0, TimeSpan.Zero));

        // byte10 = 0xA7 (Modus 2 + 39), byte11 = 0xD8 (Tag 24 + Monat 12),
        // byte12 = 0x1A (Jahr 26), byte13 = 0x25 (18:30 im 30-Min-Raster).
        command.WireLine.Should().Be(Wire("0004400000001d2a3c01a7d81a25000000"));
    }

    [Fact]
    public void Temporary_mode_rounds_1045_up_to_1100_like_java()
    {
        // RadiogramSpecification.java:196-202: min>=45 → hour++ bei unverändertem minCount.
        var command = new SetRoomTemporaryModeCommand(
            Master, 1, new Temperature(19.5), new DateTimeOffset(2026, 12, 24, 10, 45, 0, TimeSpan.Zero));

        var bytes = Convert.FromBase64String(command.WireLine[2..]);
        bytes[13].Should().Be(0x16); // 11:00 im 30-Minuten-Raster
    }

    [Fact]
    public void Configuration_encodes_all_seven_fields()
    {
        var command = new SetRoomConfigurationCommand(
            Master, 1, new Temperature(21.5), new Temperature(16.5), new Temperature(30.5),
            new Temperature(4.5), 0.0, new Temperature(12.0), 15);

        // 2B=21,5 | 21=16,5 | 3D=30,5 | 09=4,5 | 07=Offset 0 | 18=12,0 | 03=15 min/5
        command.WireLine.Should().Be(Wire("0004110000001d2a3c012b213d09071803"));
    }

    [Fact]
    public void Week_profile_first_telegram_matches_the_python_java_byte_equivalence()
    {
        var profile = new DayProfile(DayOfWeek.Saturday,
        [
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(20.5)),
            new SwitchPoint(TimeOnly.MinValue, new Temperature(4.5)), // 24:00
            new SwitchPoint(new TimeOnly(20, 0), new Temperature(30.5)),
        ]);

        var command = new SetRoomWeekTemperatureProfileCommand(profile, Master, 1);

        // 52 48 = 20,5/06:00, 13 20 = 4,5/24:00, 7A F0 = 30,5/20:00 (byte-abgleich-python-java.md:31-33).
        // Die fehlenden vier Schaltpunkte werden mit dem letzten wiederholt (SetRoomWeekTemperatureProfile.java:94-98).
        command.WireLine.Should().Be(Wire("0004100000001d2a3c0100524813207af07af07af07af07af0"));
    }

    [Fact]
    public void Week_profile_with_13_points_produces_two_telegrams()
    {
        var points = Enumerable.Range(0, 13)
            .Select(index => new SwitchPoint(new TimeOnly(0, 0).AddMinutes(60 * (index + 1)), new Temperature(20.0)))
            .ToList();
        var profile = new DayProfile(DayOfWeek.Monday, points);

        var telegrams = WeekProfileSerializer.Serialize(profile, Master, 1);

        telegrams.Should().HaveCount(2);
        telegrams[0][10].Should().Be((byte)((1 + 1) % 7)); // Montag = 2
        telegrams[1][10].Should().Be((byte)(((1 + 1) % 7) | 0x10)); // zweites Telegramm
        telegrams[0].Should().HaveCount(25); // 7 Punkte
        telegrams[1].Should().HaveCount(23); // 6 Punkte
    }

    [Fact]
    public void A_profile_with_at_most_seven_points_has_no_second_telegram()
    {
        var profile = new DayProfile(DayOfWeek.Sunday, [new SwitchPoint(new TimeOnly(6, 0), new Temperature(21.0))]);

        var act = () => new SetRoomWeekTemperatureProfileCommand(profile, Master, 1, secondTelegram: true);

        act.Should().Throw<ArgumentException>().WithMessage("*second telegram*");
    }

    [Fact]
    public void Invalid_profiles_are_rejected()
    {
        var empty = new DayProfile(DayOfWeek.Monday, []);
        var tooLong = new DayProfile(DayOfWeek.Monday, Enumerable.Repeat(
            new SwitchPoint(new TimeOnly(6, 0), new Temperature(21.0)), 14).ToList());

        var actEmpty = () => WeekProfileSerializer.Serialize(empty, Master, 1);
        var actLong = () => WeekProfileSerializer.Serialize(tooLong, Master, 1);

        actEmpty.Should().Throw<ArgumentOutOfRangeException>();
        actLong.Should().Throw<ArgumentOutOfRangeException>();
    }
}
