using MaxCube.Domain.ValueObjects;

namespace MaxCube.Domain.Aggregates;

/// <summary>
/// Das eine House pro Cube (Id konstant 0, House.java:8,11-15). Enthält die Geräte ohne
/// Raumzuordnung (roomId == 0); die Zuordnung erfolgt in MetaDataSerializer.java:69-78.
/// </summary>
public sealed class House
{
    public const int Id = 0;

    private readonly List<Device> _devices = [];
    public IReadOnlyList<Device> Devices => _devices;

    /// <summary>Lineare Suche wie House.java:22-30.</summary>
    public Device? GetDevice(RadioAddress address) => _devices.FirstOrDefault(d => d.RadioAddress == address);

    internal void AddDevice(Device device)
    {
        if (GetDevice(device.RadioAddress) is not null)
        {
            throw new Exceptions.DomainValidationException(
                Exceptions.DomainValidationCodes.InvalidDeviceRadioAddress,
                $"Device {device.RadioAddress} already exists in house.");
        }

        _devices.Add(device);
        device.AssignToRoom(Id);
    }

    internal void RemoveDevice(Device device) => _devices.Remove(device);
}
