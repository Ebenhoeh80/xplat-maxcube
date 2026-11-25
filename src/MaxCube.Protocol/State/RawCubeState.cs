using System.Text;
using MaxCube.Protocol.Handshake;

namespace MaxCube.Protocol.State;

/// <summary>
/// Raw connection state before domain mapping. Fields are kept as wire strings/bytes,
/// mirroring RawCubeState.java:8-60. No device semantics are interpreted here; that is
/// the job of the later state mapping (AP-1.4).
/// </summary>
public sealed class RawCubeState
{
    private readonly Dictionary<int, string> _configurations = new();
    private readonly Dictionary<int, MetadataBlock> _metadataBlocks = new();

    /// <summary>Header body from the first H: line, without the "H:" prefix.</summary>
    public string? Header { get; internal set; }

    public CubeHello? Hello { get; internal set; }

    /// <summary>Base64 body of the L: line that ended the initial state import.</summary>
    public string? DeviceList { get; internal set; }

    public IReadOnlyDictionary<int, string> Configurations => _configurations;

    public IReadOnlyDictionary<int, MetadataBlock> MetadataBlocks => _metadataBlocks;

    public int? MetadataCount { get; internal set; }

    public bool IsComplete => DeviceList is not null
        && (!MetadataCount.HasValue || Enumerable.Range(0, MetadataCount.Value).All(_metadataBlocks.ContainsKey));

    public string? GetDeviceConfiguration(int radioAddress)
        => _configurations.TryGetValue(radioAddress, out var config) ? config : null;

    public void SetDeviceConfiguration(int radioAddress, string config)
        => _configurations[radioAddress] = config;

    /// <summary>
    /// Replaces a block, including a repeated index (Metadata.setBlock, Metadata.java:43-46).
    /// A differing total count is an inconsistency and is rejected.
    /// </summary>
    public void SetMetadataBlock(int index, int count, byte[] payload)
    {
        if (MetadataCount.HasValue && MetadataCount.Value != count)
        {
            throw new ArgumentException(
                $"Inconsistent metadata count: expected {MetadataCount.Value}, received {count}.", nameof(count));
        }

        MetadataCount = count;
        _metadataBlocks[index] = new MetadataBlock(index, count, payload);
    }

    /// <summary>
    /// Concatenated base64 text of all known blocks in index order. Missing blocks are an
    /// error: silently concatenating fewer blocks would produce corrupt metadata.
    /// </summary>
    public string GetMetadataBase64()
    {
        if (!MetadataCount.HasValue)
        {
            throw new InvalidOperationException("No metadata blocks were received.");
        }

        var builder = new StringBuilder();
        for (var index = 0; index < MetadataCount.Value; index++)
        {
            if (!_metadataBlocks.TryGetValue(index, out var block))
            {
                throw new InvalidOperationException($"Metadata block {index} of {MetadataCount.Value} is missing.");
            }

            builder.Append(block.PayloadText);
        }

        return builder.ToString();
    }
}
