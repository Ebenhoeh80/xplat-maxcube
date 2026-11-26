using MaxCube.Domain.Enums;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Domain.Configuration;

/// <summary>Konfiguration einer Taste des Cube-Push-Buttons (PushButtonKeyMode.java:4-10).</summary>
public sealed record PushButtonKeyConfiguration(PushButtonKeyMode Mode, Temperature? Temperature);

public sealed record PushButtonConfiguration(
    PushButtonKeyConfiguration Upper,
    PushButtonKeyConfiguration Lower);

/// <summary>Eine Zeitzonenregel (Sommer- oder Winterzeit) im 12-Byte-Format des Cubes.</summary>
public sealed record TimeZoneRule(string Name, int Month, int DayOfWeekFromSunday, int Hour, int OffsetSeconds);

/// <summary>Winter- und Sommerzeit; identische Regeln bedeuten „keine Sommerzeit“.</summary>
public sealed record TimeZoneConfiguration(TimeZoneRule Winter, TimeZoneRule Summer)
{
    public bool UsesDaylightSaving => Winter != Summer;
}

/// <summary>Voreinstellungen für „Zeitzone / Sommerzeit“ (Regel: letzter Sonntag, Cube-Format).</summary>
public static class TimeZonePresets
{
    public static readonly IReadOnlyDictionary<string, TimeZoneConfiguration> All =
        new Dictionary<string, TimeZoneConfiguration>
        {
            ["Mitteleuropa (CET/CEST)"] = new(
                new TimeZoneRule("CET", 10, 0, 3, 3600), new TimeZoneRule("CEST", 3, 0, 2, 7200)),
            ["Westeuropa (GMT/BST)"] = new(
                new TimeZoneRule("GMT", 10, 0, 2, 0), new TimeZoneRule("BST", 3, 0, 1, 3600)),
            ["Osteuropa (EET/EEST)"] = new(
                new TimeZoneRule("EET", 10, 0, 4, 7200), new TimeZoneRule("EEST", 3, 0, 3, 10800)),
            ["Mitteleuropa ohne Sommerzeit"] = new(
                new TimeZoneRule("CET", 10, 0, 3, 3600), new TimeZoneRule("CET", 10, 0, 3, 3600)),
        };
}

/// <summary>Netzwerkkonfiguration des Cubes (UDP-Opcodes c/C, docs/maxcube-protocol.md:484-485).</summary>
public sealed record CubeNetworkConfiguration(
    string IpAddress,
    string SubnetMask,
    string Gateway,
    string Dns1,
    string Dns2,
    bool Dhcp,
    string? ActiveIp = null);
