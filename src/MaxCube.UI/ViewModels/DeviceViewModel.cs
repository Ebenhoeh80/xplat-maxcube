using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Enums;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>Ansichtsmodell eines physischen Geräts (Seriennummer/Funkadresse/Status).</summary>
public sealed partial class DeviceViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _loc;
    private DeviceType _deviceType;
    private DeviceStateInfo _stateInfo;
    private RadioState _radioState;

    public DeviceViewModel(Device device, Localizer? localizer = null)
    {
        _loc = localizer ?? new Localizer("de");
        _loc.PropertyChanged += OnLanguageChanged;
        Address = device.RadioAddress.Value.ToString("X6");
        SerialNumber = device.SerialNumber.Value;
        _deviceType = device.DeviceType;
        RoomId = device.RoomId;
        Update(device);
    }

    public string Address { get; }

    public string SerialNumber { get; }

    public DeviceType DeviceType => _deviceType;

    public string Type => _loc.Get(EnumLabels.Key(_deviceType));

    public int RoomId { get; }

    [ObservableProperty]
    private string name = string.Empty;

    public string State => _loc.Get(EnumLabels.Key(_stateInfo));

    public string RadioState => _loc.Get(EnumLabels.Key(_radioState));

    public bool IsHealthy => _stateInfo == DeviceStateInfo.Valid
        && _radioState == global::MaxCube.Domain.Enums.RadioState.Ok;

    public void Update(Device device)
    {
        Name = device.Name;
        _stateInfo = device.StateInfo;
        _radioState = device.RadioState;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(RadioState));
        OnPropertyChanged(nameof(IsHealthy));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Item[]" or nameof(Localizer.Language) or null)
        {
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(RadioState));
        }
    }

    public void Dispose() => _loc.PropertyChanged -= OnLanguageChanged;
}
