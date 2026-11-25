using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>Ansichtsmodell eines Raums; enthält keine Avalonia-Typen.</summary>
public sealed partial class RoomViewModel : ObservableObject
{
    private readonly Localizer _loc;

    public RoomViewModel(Room room, Localizer? localizer = null)
    {
        _loc = localizer ?? new Localizer("de");
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

    [ObservableProperty]
    private string controlMode = string.Empty;

    [ObservableProperty]
    private string temperatureMode = string.Empty;

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
        ControlMode = room.ControlMode.ToString();
        TemperatureMode = room.TemperatureMode.ToString();
        HasDeviceError = room.Devices.Any(device =>
            device.StateInfo != DeviceStateInfo.Valid || device.RadioState != RadioState.Ok);
    }

    public override string ToString() => $"{Name} ({SetPoint:F1} °C)";
}
