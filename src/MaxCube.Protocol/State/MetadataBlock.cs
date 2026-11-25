namespace MaxCube.Protocol.State;

/// <summary>
/// One M: block. The payload is the unmodified ASCII text of the base64 chunk, exactly as
/// RawCubeState/Metadata keep it in the Java original (CubeConnection.java:172-180,
/// RawCubeState.java:40-42). The chunks are concatenated before base64 decoding.
/// </summary>
public sealed record MetadataBlock(int Index, int Count, byte[] Payload)
{
    public string PayloadText => System.Text.Encoding.ASCII.GetString(Payload);
}
