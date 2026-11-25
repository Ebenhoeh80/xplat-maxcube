using MaxCube.Application.Abstractions;
using MaxCube.Domain.Aggregates;
using MaxCube.Domain.Configuration;
using MaxCube.Domain.ValueObjects;
using MaxCube.Protocol.Commands;
using MaxCube.Protocol.Exceptions;
using MaxCube.Protocol.State;

namespace MaxCube.Infrastructure.Connectivity;

/// <summary>
/// Erweiterte Cube-Funktionen (<see cref="ICubeAdminGateway"/>). Alle Aufrufe laufen über
/// <c>SendAsync</c> und damit durch dasselbe Gate, dieselbe Queue und denselben Worker wie
/// die Kernbefehle; es gibt keinen zweiten Schreibpfad.
/// </summary>
public sealed partial class CubeGateway
{
    /// <summary>Länge eines einzelnen Anlernfensters; das Gesamtfenster wird in der Anwendung aufgebaut.</summary>
    public static readonly TimeSpan MinimumInclusionWindow = TimeSpan.FromSeconds(1);

    public Task SetRoomConfigurationAsync(
        int roomId, RadioAddress master, RoomConfigurationValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        return SendAsync(
            new SetRoomConfigurationCommand(
                master, roomId, values.Comfort, values.Eco, values.Maximum, values.Minimum,
                values.Offset, values.WindowOpen, values.WindowOpenMinutes),
            cancellationToken);
    }

    public Task SetValveFunctionsAsync(
        int roomId, RadioAddress master, ValveFunctionValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        return SendAsync(
            new SetRoomValveFunctionsCommand(
                master, roomId, values.BoostMinutes, values.BoostValveAngle, values.DecalcificationDay,
                values.DecalcificationHour, values.MaxValveRaw, values.ValveOffsetRaw),
            cancellationToken);
    }

    public Task SetDisplayModeAsync(
        RadioAddress wallThermostat, bool showActualTemperature, CancellationToken cancellationToken = default)
        => SendAsync(new SetDisplayModeCommand(wallThermostat, showActualTemperature), cancellationToken);

    public async Task<FoundDevice?> SearchNewDeviceAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var seconds = (int)Math.Clamp(Math.Ceiling(window.TotalSeconds), 1, 0xFFFF);
        var body = await SendAsync(new StartInclusionCommand(seconds), cancellationToken).ConfigureAwait(false);
        var found = NewDeviceDecoder.Decode(body);
        if (found is null)
        {
            return null;
        }

        // Nach einem gefundenen Gerät bleibt der Cube im Anlernmodus, bis x: ihn beendet
        // (FetchNewDevicesCubeCommand.java:48-62). Ohne das Beenden würden alle folgenden
        // Befehle mit CubeIsInInclusionModeException abgelehnt.
        await EndInclusionAsync(cancellationToken).ConfigureAwait(false);
        return new FoundDevice(found.DeviceType, found.NumericDeviceType, found.RadioAddress, found.SerialNumber);
    }

    public async Task EndInclusionAsync(CancellationToken cancellationToken = default)
    {
        if (_queue is { IsInclusionMode: true })
        {
            await SendAsync(new CancelInclusionCommand(), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SetGroupAddressAsync(RadioAddress device, int groupId, CancellationToken cancellationToken = default)
        => SendAsync(new SetGroupAddressCommand(device, groupId), cancellationToken);

    public Task RemoveGroupAddressAsync(RadioAddress device, int roomId, CancellationToken cancellationToken = default)
        => SendAsync(new RemoveGroupAddressCommand(device, roomId), cancellationToken);

    public Task SetLinkPartnerAsync(
        RadioAddress device, RadioAddress partner, int partnerNumericType, bool add,
        CancellationToken cancellationToken = default)
        => SendAsync(new LinkPartnerCommand(device, partner, partnerNumericType, add), cancellationToken);

    public Task SetLinkPartnerInRoomAsync(
        RadioAddress master, int roomId, RadioAddress partner, int partnerNumericType, bool add,
        CancellationToken cancellationToken = default)
        => SendAsync(new LinkPartnerInRoomCommand(master, roomId, partner, partnerNumericType, add), cancellationToken);

    public Task DeleteDevicesAsync(
        IReadOnlyList<RadioAddress> devices, bool factoryReset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return SendAsync(
            new DeleteDevicesCommand(devices.Select(address => address.Value).ToArray(), factoryReset),
            cancellationToken);
    }

    public async Task WriteMetadataAsync(Cube cube, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cube);
        var original = _sessionMetadata
            ?? throw new InvalidOperationException(
                "The cube sent no metadata in this session; structural changes are not possible.");

        // Sicherheitsnetz vor dem ersten gesendeten Byte: der unveränderte Zustand muss sich
        // byte-identisch reproduzieren lassen. Schlägt das fehl, wurde nichts gesendet.
        MetadataEncoder.VerifyRoundTrip(original);

        var encoded = MetadataEncoder.EncodeBase64(MetadataEncoder.FromCube(cube));
        var blocks = MetadataEncoder.SplitIntoBlocks(encoded);
        for (var index = 0; index < blocks.Count; index++)
        {
            try
            {
                await SendAsync(new SendMetadataBlockCommand(index, blocks[index]), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Ein Teilschreiben lässt den Cube mit gemischten Blöcken zurück; der Fehlertext
                // sagt das ausdrücklich, damit die Anwendung neu liest statt weiterzuarbeiten.
                throw new CubeCommunicationFailedException(
                    $"Metadata block {index + 1}/{blocks.Count} failed; the cube may hold partial metadata. Reconnect to reload.",
                    exception);
            }
        }

        _sessionMetadata = encoded;
    }

    public async Task<PushButtonConfiguration> ReadPushButtonAsync(
        RadioAddress cubeAddress, CancellationToken cancellationToken = default)
    {
        var configuration = await ReadCubeConfigurationAsync(cubeAddress, cancellationToken).ConfigureAwait(false);
        return CubeConfigurationCodec.ReadPushButton(configuration);
    }

    public async Task SetPushButtonAsync(
        RadioAddress cubeAddress, PushButtonConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var current = await ReadCubeConfigurationAsync(cubeAddress, cancellationToken).ConfigureAwait(false);
        var payload = CubeConfigurationCodec.BuildPushButtonPayload(current, configuration);
        await SendAsync(new SetPushButtonConfigurationCommand(payload), cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimeZoneConfiguration> ReadTimeZoneAsync(
        RadioAddress cubeAddress, CancellationToken cancellationToken = default)
    {
        var configuration = await ReadCubeConfigurationAsync(cubeAddress, cancellationToken).ConfigureAwait(false);
        return CubeConfigurationCodec.ReadTimeZone(configuration);
    }

    public Task SetTimeZoneAsync(
        RadioAddress cubeAddress,
        TimeZoneConfiguration configuration,
        DateTimeOffset? setTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var payload = CubeConfigurationCodec.BuildTimeZonePayload(configuration);
        var seconds = setTimeUtc is { } time ? (int)Math.Clamp(time.ToUnixTimeSeconds(), 0, int.MaxValue) : 0;
        return SendAsync(new SetTimezoneConfigurationCommand(payload, seconds), cancellationToken);
    }

    public Task<string> QueryNtpServersAsync(CancellationToken cancellationToken = default)
        => SendAsync(new QueryNtpServerCommand(), cancellationToken);

    public async Task SetNtpServersAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(servers);
        await SendAsync(new SetNtpServerCommand(servers.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadCubeConfigurationAsync(RadioAddress cubeAddress, CancellationToken cancellationToken)
    {
        var body = await SendAsync(new GetConfigurationCommand(cubeAddress.Value), cancellationToken)
            .ConfigureAwait(false);
        return CubeConfigurationCodec.DecodeResponse(body);
    }
}
