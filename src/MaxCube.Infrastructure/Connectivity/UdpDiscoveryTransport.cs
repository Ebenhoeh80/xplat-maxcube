using System.Net;
using System.Net.Sockets;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Models;
using MaxCube.Application.Services;

namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// UDP-Adapter für <see cref="IDiscoveryTransport"/>. Sendet den belegten Request an
/// Broadcast und Multicast (TTL 5), dreimal im Abstand von 50 ms, und sammelt Antworten
/// bis zum Zeitbudget. Es wird ausschließlich gelesen/gesendet – nie Firmware geschrieben.
/// </summary>
public sealed class UdpDiscoveryTransport : IDiscoveryTransport
{
    private static readonly IPAddress Broadcast = IPAddress.Parse(CubeDiscoveryProtocol.BroadcastAddress);
    private static readonly IPAddress Multicast = IPAddress.Parse(CubeDiscoveryProtocol.MulticastGroup);

    public async Task<IReadOnlyList<DiscoveryDatagram>> ProbeAsync(
        byte[] request, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        using var port = await UdpPortGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        return await ProbeCoreAsync(request, duration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DiscoveryDatagram>> ProbeCoreAsync(
        byte[] request, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Der eQ-3 Cube antwortet auf den Absenderport 23272 (CubeFinder.java:40). Ein
        // ephemeraler lokaler Port lässt die Antwort unerreichbar – Hardwarebeleg 25.11.2026.
        using var client = new UdpClient(new IPEndPoint(IPAddress.Any, CubeDiscoveryProtocol.UdpPort));
        client.EnableBroadcast = true;

        var joinedMulticast = false;
        try
        {
            client.JoinMulticastGroup(Multicast, CubeDiscoveryProtocol.MulticastTtl);
            joinedMulticast = true;
        }
        catch (SocketException)
        {
            // Einige Netzwerke (Container, IPv6-only) erlauben keinen Join;
            // der Broadcast-Pfad bleibt dann nutzbar.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration < TimeSpan.Zero ? TimeSpan.Zero : duration);

        try
        {
            for (var attempt = 0; attempt < CubeDiscoveryProtocol.ResendCount; attempt++)
            {
                await client.SendAsync(
                    request, request.Length, new IPEndPoint(Broadcast, CubeDiscoveryProtocol.UdpPort))
                    .ConfigureAwait(false);
                if (joinedMulticast)
                {
                    await client.SendAsync(
                        request, request.Length, new IPEndPoint(Multicast, CubeDiscoveryProtocol.UdpPort))
                        .ConfigureAwait(false);
                }

                if (attempt < CubeDiscoveryProtocol.ResendCount - 1)
                {
                    await Task.Delay(CubeDiscoveryProtocol.ResendInterval, timeout.Token)
                        .ConfigureAwait(false);
                }
            }

            var replies = new List<DiscoveryDatagram>();
            while (!timeout.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // Zeitbudget abgelaufen: keine Antworten mehr erwarten.
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                if (received.RemoteEndPoint.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    replies.Add(new DiscoveryDatagram(
                        received.Buffer, received.RemoteEndPoint.Address.ToString()));
                }
            }

            return replies;
        }
        finally
        {
            if (joinedMulticast)
            {
                try
                {
                    client.DropMulticastGroup(Multicast);
                }
                catch (SocketException)
                {
                    // Das Beenden darf nicht an einem Multicast-Teilnehmer scheitern.
                }
            }
        }
    }
}
