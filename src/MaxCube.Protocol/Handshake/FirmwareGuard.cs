using MaxCube.Protocol.Exceptions;

namespace MaxCube.Protocol.Handshake;

/// <summary>
/// Enforces the firmware window 273..275 (max.local.properties:17-18,
/// CubeConnection.java:57,64). Write-capable features must additionally stay
/// behind their feature flags until a golden capture exists.
/// </summary>
public static class FirmwareGuard
{
    public static void EnsureSupported(int firmwareVersion, CubeConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (firmwareVersion < options.MinimumFirmwareVersion || firmwareVersion > options.MaximumFirmwareVersion)
        {
            throw new IncompatibleFirmwareException(
                firmwareVersion, options.MinimumFirmwareVersion, options.MaximumFirmwareVersion);
        }
    }
}
