using System.Net;
using System.Net.Sockets;
using MaxCube.Application.Abstractions;
using MaxCube.Domain.Configuration;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;

namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// Liest und setzt die Netzwerkkonfiguration des Cubes per UDP-Unicast (Opcodes c/C).
/// Nur auf ausdrücklichen Benutzerbefehl; es gibt keinen Broadcast und keinen Firmware-Opcode.
/// Zeitparameter nach CubeUdpProxy.java:23-27: 2 s Timeout, bis zu 5 Versuche.
/// </summary>
public sealed class UdpNetworkConfigurator : ICubeNetworkConfigurator
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);
    private const int MaxAttempts = 5;
    private readonly int _port;

    public UdpNetworkConfigurator(int port = CubeUdpCodec.Port) => _port = port;

    public async Task<CubeNetworkConfiguration> ReadAsync(
        string serialNumber, string ipAddress, CancellationToken cancellationToken = default)
    {
        var data = await RequestAsync(
            serialNumber, ipAddress, CubeUdpCodec.GetNetworkConfiguration, null, cancellationToken).ConfigureAwait(false);
        return CubeUdpCodec.DecodeNetwork(data);
    }

    public async Task WriteAsync(
        string serialNumber, string ipAddress, CubeNetworkConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var current = await RequestAsync(
            serialNumber, ipAddress, CubeUdpCodec.GetNetworkConfiguration, null, cancellationToken).ConfigureAwait(false);
        var payload = CubeUdpCodec.EncodeNetwork(configuration, current);
        await RequestAsync(
            serialNumber, ipAddress, CubeUdpCodec.SetNetworkConfiguration, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> RequestAsync(
        string serialNumber, string ipAddress, byte opcode, byte[]? payload, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("A cube IPv4 address is required.", nameof(ipAddress));
        }

        var request = CubeUdpCodec.BuildRequest(serialNumber, opcode, payload);
        try
        {
            // FAKT (Hardware 25.11.2026): Der Cube antwortet auf Port 23272; ein ephemeraler
            // lokaler Port erhält keine Antwort. Die Sperre verhindert Parallelzugriff im Prozess.
            using var port = await UdpPortGate.EnterAsync(cancellationToken).ConfigureAwait(false);
            using var client = new UdpClient(new IPEndPoint(IPAddress.Any, CubeUdpCodec.Port));
            var target = new IPEndPoint(address, _port);
            Exception? last = null;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await client.SendAsync(request, request.Length, target).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(AttemptTimeout);
                try
                {
                    var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (!result.RemoteEndPoint.Address.Equals(address))
                    {
                        continue; // Antwort einer anderen Quelle ignorieren
                    }

                    return CubeUdpCodec.ParseResponse(result.Buffer, serialNumber, opcode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    last = new TimeoutException("No UDP response from the cube.");
                }
                catch (CubeCommunicationFailedException exception)
                {
                    last = exception;
                }
                catch (SocketException exception)
                {
                    // ICMP/Netzwerkfehler während des Empfangs: nächster Versuch.
                    last = exception;
                }
            }

            throw new CubeCommunicationFailedException(
                "The cube did not answer the UDP request.", last ?? new TimeoutException("No UDP response from the cube."));
        }
        catch (SocketException exception)
        {
            // Bind-Konflikt oder dauerhafter Socketfehler: klar benannt statt rohem Typ.
            throw new CubeCommunicationFailedException(
                $"MAX1008: UDP-Fehler auf Port {CubeUdpCodec.Port} ({exception.SocketErrorCode}).", exception);
        }
    }
}
