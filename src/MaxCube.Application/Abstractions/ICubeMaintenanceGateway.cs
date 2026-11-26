namespace MaxCube.Application.Abstractions;

/// <summary>
/// Wartungseingriffe am Cube, die nur nach ausdrücklicher, benutzerinitiierter Bestätigung
/// ausgeführt werden dürfen. Bewusst von den normalen Admin-Befehlen getrennt.
/// </summary>
public interface ICubeMaintenanceGateway
{
    /// <summary>UDP-Opcode R (CubeUdpOpcode.Reboot) an den verbundenen Cube.</summary>
    Task RebootAsync(string serialNumber, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>TCP-Opcode a: (ResetGatewayCommand.java:11-12); erwartet kein ACK.</summary>
    Task FactoryResetAsync(CancellationToken cancellationToken = default);
}
