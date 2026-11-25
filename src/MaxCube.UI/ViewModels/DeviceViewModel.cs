using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.Domain.Aggregates;

namespace MaxCube.UI.ViewModels;

/// <summary>Ansichtsmodell eines physischen Geräts (Seriennummer/Funkadresse/Status).</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    public DeviceViewModel(Device device)
    {
        Address = device.RadioAddress.Value.ToString("X6");
        SerialNumber = device.SerialNumber.Value;
        Type = device.DeviceType.ToString();
        RoomId = device.RoomId;
        Update(device);
    }

    public string Address { get; }

    public string SerialNumber { get; }

    public string Type { get; }

    public int RoomId { get; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string state = string.Empty;

    [ObservableProperty]
    private string radioState = string.Empty;

    public bool IsHealthy => State == global::MaxCube.Domain.Enums.DeviceStateInfo.Valid.ToString()
        && RadioState == global::MaxCube.Domain.Enums.RadioState.Ok.ToString();

    public void Update(Device device)
    {
        Name = device.Name;
        State = device.StateInfo.ToString();
        RadioState = device.RadioState.ToString();
        OnPropertyChanged(nameof(IsHealthy));
    }
}
