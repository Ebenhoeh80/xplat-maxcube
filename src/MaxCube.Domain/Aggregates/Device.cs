using MaxCube.Domain.Enums;
using MaxCube.Domain.Exceptions;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.Domain.Aggregates;

/// <summary>
/// Physisches Gerät. Identität ist die Funkadresse (Device.java:27-35, domain-model.md:424).
/// Default-Zustand: NotInitialized/Ok (Device.java:24-25).
/// </summary>
public sealed class Device
{
    public Device(
        RadioAddress radioAddress,
        DeviceType deviceType,
        int numericDeviceType,
        SerialNumber serialNumber,
        string name,
        int roomId)
    {
        RadioAddress = radioAddress;
        DeviceType = deviceType;
        NumericDeviceType = numericDeviceType;
        SerialNumber = serialNumber;
        Name = ValidateName(name);
        RoomId = roomId;
    }

    public RadioAddress RadioAddress { get; }
    public DeviceType DeviceType { get; }
    public int NumericDeviceType { get; }
    public SerialNumber SerialNumber { get; }
    public string Name { get; private set; }
    public int RoomId { get; private set; }
    public DeviceStateInfo StateInfo { get; private set; } = DeviceStateInfo.NotInitialized;
    public RadioState RadioState { get; private set; } = RadioState.Ok;

    /// <summary>Name darf nicht null sein und höchstens 32 Zeichen haben (ConfigureDeviceValidator.java:148-154).</summary>
    public static string ValidateName(string? name)
    {
        if (name is null)
        {
            throw new DomainValidationException(DomainValidationCodes.NameIsNull, "Device name is null.");
        }

        if (name.Length > DomainLimits.MaxNameLength)
        {
            throw new DomainValidationException(
                DomainValidationCodes.NameTooLong,
                $"Device name exceeds {DomainLimits.MaxNameLength} characters.");
        }

        return name;
    }

    internal void AssignToRoom(int roomId) => RoomId = roomId;

    internal void Rename(string name) => Name = ValidateName(name);

    internal void SetState(DeviceStateInfo stateInfo, RadioState radioState)
    {
        StateInfo = stateInfo;
        RadioState = radioState;
    }

    public override string ToString() => $"{Name} ({RadioAddress})";
}
