using MaxCube.Application.Models;

namespace MaxCube.UI.ViewModels;

/// <summary>Ein Treffer der UDP-Suche für die Auswahlliste; enthält keine Netzwerkobjekte.</summary>
public sealed class DiscoveredCubeViewModel
{
    public DiscoveredCubeViewModel(DiscoveredCube cube)
    {
        SerialNumber = cube.SerialNumber;
        IpAddress = cube.IpAddress;
        FirmwareVersion = cube.FirmwareVersion;
        Bootloader = cube.Bootloader;
    }

    public string SerialNumber { get; }

    public string IpAddress { get; }

    public int FirmwareVersion { get; }

    public bool Bootloader { get; }

    public string Display
        => $"{SerialNumber} · {IpAddress} · FW {FirmwareVersion}{(Bootloader ? " · Bootloader" : string.Empty)}";

    public override string ToString() => Display;
}
