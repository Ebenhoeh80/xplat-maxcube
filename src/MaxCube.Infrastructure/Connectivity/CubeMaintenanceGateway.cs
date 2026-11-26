using System.Net;
using System.Net.Sockets;
using MaxCube.Application.Abstractions;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;

namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// Benutzerinitiierte Wartung: Neustart per UDP-Opcode R an die verbundene IP, Werksreset per
/// TCP-Opcode a: über die bestehende Cube-Kommandowarteschlange. Es gibt keine automatische
/// Ausführung, keinen Broadcast und keine Wiederholung des Neustarts.
/// </summary>
public sealed class CubeMaintenanceGateway : ICubeMaintenanceGateway
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);
    private readonly CubeGateway _gateway;
    private readonly int _port;

    public CubeMaintenanceGateway(CubeGateway gateway, int port = CubeUdpCodec.Port)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _port = port;
    }

    public async Task RebootAsync(
        string serialNumber, string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("A cube IPv4 address is required.", nameof(ipAddress));
        }

        var request = CubeUdpCodec.BuildRequest(serialNumber, CubeUdpCodec.Reboot);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Any, CubeUdpCodec.Port));
        await client.SendAsync(request, request.Length, new IPEndPoint(address, _port)).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        try
        {
            var response = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            if (!response.RemoteEndPoint.Address.Equals(address))
            {
                throw new CubeCommunicationFailedException("Reboot acknowledgement came from an unexpected source.");
            }

            CubeUdpCodec.ParseResponse(response.Buffer, serialNumber, CubeUdpCodec.Reboot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Genau einmal gesendet. Kein Retry: ein zweiter Reboot wäre hardware riskant.
            throw new CubeCommunicationFailedException(
                "Reboot was sent, but no acknowledgement was received.", new TimeoutException("No UDP response from the cube."));
        }
    }

    public Task FactoryResetAsync(CancellationToken cancellationToken = default)
        => _gateway.ResetGatewayAsync(cancellationToken);
}
