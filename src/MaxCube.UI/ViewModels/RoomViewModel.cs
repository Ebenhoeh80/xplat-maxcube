using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>Ansichtsmodell eines Raums; enthält keine Avalonia-Typen.</summary>
public sealed partial class RoomViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _loc;
    private RoomControlMode _controlMode = RoomControlMode.NotAvailable;
    private RoomTemperatureMode _temperatureMode = RoomTemperatureMode.NotAvailable;

    public RoomViewModel(Room room, Localizer? localizer = null)
    {
        _loc = localizer ?? new Localizer("de");
        _loc.PropertyChanged += OnLanguageChanged;
        Id = room.Id;
        Name = room.Name;
        MasterAddress = room.MasterRadioAddress.Value.ToString("X6");
        Update(room);
    }

    public int Id { get; }

    public string Name { get; }

    public string MasterAddress { get; }

    [ObservableProperty]
    private double setPoint;

    [ObservableProperty]
    private double actual;

    [ObservableProperty]
    private string actualText = "Ist: —";

    public string ControlMode => _loc.Get(EnumLabels.Key(_controlMode));

    public string TemperatureMode => _loc.Get(EnumLabels.Key(_temperatureMode));

    [ObservableProperty]
    private bool hasDeviceError;

    /// <summary>Auswahl für die Haussteuerung (mehrere Räume gemeinsam).</summary>
    [ObservableProperty]
    private bool isHouseSelected;

    public void Update(Room room)
    {
        SetPoint = room.SetPointTemperature.Value;
        Actual = room.ActualTemperature.Value;
        ActualText = room.HasActualTemperature ? _loc.Format("ActualValue", Actual) : _loc.Get("ActualNone");
        _controlMode = room.ControlMode;
        _temperatureMode = room.TemperatureMode;
        OnPropertyChanged(nameof(ControlMode));
        OnPropertyChanged(nameof(TemperatureMode));
        HasDeviceError = room.Devices.Any(device =>
            device.StateInfo != DeviceStateInfo.Valid || device.RadioState != RadioState.Ok);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Item[]" or nameof(Localizer.Language) or null)
        {
            OnPropertyChanged(nameof(ActualText));
            OnPropertyChanged(nameof(ControlMode));
            OnPropertyChanged(nameof(TemperatureMode));
        }
    }

    public void Dispose() => _loc.PropertyChanged -= OnLanguageChanged;

    public override string ToString() => $"{Name} ({SetPoint:F1} °C)";
}
