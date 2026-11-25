using FluentAssertions;
using MaxCube.Domain.Enums;
using MaxCube.Protocol.State;
using Xunit;
using DayOfWeek = MaxCube.Domain.Enums.DayOfWeek;

namespace MaxCube.Protocol.Tests;

public class WeekProfileDecoderTests
{
    private const int HeaderLength = 18;
    private const int ProfileOffset = 11;
    private const int EntriesPerDay = 13;
    private const int ProfileLength = EntriesPerDay * 2 * 7;

    [Fact]
    public void A_java_shaped_profile_decodes_all_seven_days_in_saturday_first_order()
    {
        var config = BuildConfig(day =>
        {
            if (day == 0) return [Entry(6, 0, 20.0), Entry(0, 0, 4.5)];
            return [Entry(6, 0, 17.0), Entry(0, 0, 21.5)];
        });

        var profile = WeekProfileDecoder.Decode(config.AsSpan(HeaderLength));

        profile.Should().NotBeNull();
        profile!.Days.Should().HaveCount(7);
        var saturday = profile.GetDay(DayOfWeek.Saturday)!;
        saturday.Points.Should().HaveCount(2);
        saturday.Points[0].Until.Should().Be(new TimeOnly(6, 0));
        saturday.Points[0].Temperature.Value.Should().Be(20.0);
        saturday.Points[1].Until.Should().Be(TimeOnly.MinValue);
        saturday.Points[1].Temperature.Value.Should().Be(4.5);
        profile.GetDay(DayOfWeek.Monday)!.Points[0].Temperature.Value.Should().Be(17.0);
    }

    [Fact]
    public void Entries_after_the_first_midnight_are_ignored_without_shifting_the_next_day()
    {
        var lateEntries = Enumerable.Repeat(Entry(12, 0, 25.0), EntriesPerDay - 2).ToArray();
        var config = BuildConfig(day => day == 0
            ? [Entry(6, 0, 20.0), Entry(0, 0, 4.5), .. lateEntries]
            : [Entry(6, 0, 17.0), Entry(0, 0, 21.5)]);

        var profile = WeekProfileDecoder.Decode(config.AsSpan(HeaderLength))!;

        // WeekTemperatureProfileBuilder.java:29-43: der erste 00:00 beendet den Tag;
        // danach wird nichts mehr übernommen. Der Cursor läuft aber über alle 13 Einträge,
        // sonst würde der Folgetag aus den Resteinträgen dekodiert.
        profile.GetDay(DayOfWeek.Saturday)!.Points.Should().HaveCount(2);
        profile.GetDay(DayOfWeek.Sunday)!.Points.Should().HaveCount(2);
        profile.GetDay(DayOfWeek.Sunday)!.Points[0].Temperature.Value.Should().Be(17.0);
    }

    [Fact]
    public void Off_and_on_wire_values_are_mapping_to_the_domain_special_states()
    {
        var config = BuildConfig(_ => [Entry(6, 0, 4.5), Entry(0, 0, 30.5)]);

        var saturday = WeekProfileDecoder.Decode(config.AsSpan(HeaderLength))!.GetDay(DayOfWeek.Saturday)!;

        saturday.Points[0].Temperature.IsOff.Should().BeTrue();
        saturday.Points[1].Temperature.IsOn.Should().BeTrue();
    }

    [Fact]
    public void An_incomplete_config_block_yields_no_profile()
    {
        var bytes = new byte[HeaderLength + ProfileOffset + ProfileLength - 2];

        WeekProfileDecoder.Decode(bytes.AsSpan(HeaderLength)).Should().BeNull();
    }

    private static byte[] BuildConfig(Func<int, byte[][]> entriesPerDay)
    {
        var config = new byte[ProfileOffset + ProfileLength];
        for (var day = 0; day < 7; day++)
        {
            var entries = entriesPerDay(day);
            for (var index = 0; index < EntriesPerDay; index++)
            {
                var entry = index < entries.Length ? entries[index] : Entry(6, 0, 21.5);
                var offset = ProfileOffset + ((day * EntriesPerDay) + index) * 2;
                config[offset] = entry[0];
                config[offset + 1] = entry[1];
            }
        }

        var bytes = new byte[HeaderLength + config.Length];
        config.CopyTo(bytes, HeaderLength);
        return bytes;
    }

    /// <summary>value = (temperature*2 &lt;&lt; 9) | ((hour*60+minute)/5), big-endian.</summary>
    private static byte[] Entry(int hour, int minute, double temperature)
    {
        var value = ((int)Math.Round(temperature * 2) << 9) | (((hour * 60) + minute) / 5);
        return [(byte)(value >> 8), (byte)value];
    }
}
