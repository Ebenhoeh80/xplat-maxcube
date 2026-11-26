using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.Application.Models;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>Ein Treffer der UDP-Suche für die Auswahlliste; enthält keine Netzwerkobjekte.</summary>
public sealed class DiscoveredCubeViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _loc;

    public DiscoveredCubeViewModel(DiscoveredCube cube, Localizer? localizer = null)
    {
        _loc = localizer ?? new Localizer("de");
        _loc.PropertyChanged += OnLanguageChanged;
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
        => $"{SerialNumber} · {IpAddress} · {_loc.Get("FirmwareShort")} {FirmwareVersion}"
            + (Bootloader ? $" · {_loc.Get("Bootloader")}" : string.Empty);

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Item[]" or nameof(Localizer.Language) or null)
        {
            OnPropertyChanged(nameof(Display));
        }
    }

    public void Dispose() => _loc.PropertyChanged -= OnLanguageChanged;

    public override string ToString() => Display;
}
