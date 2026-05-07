namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Encodes/decodes the Postgres xmin <c>uint</c> rowversion as a base64 string
/// for the wire (ETag header + ApplicationResponse.Version field). The format
/// is little-endian 4 bytes; clients MUST treat it as opaque.
/// </summary>
public static class VersionEncoding
{
    public static string Encode(uint version)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, version);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string raw, out uint version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        Span<byte> bytes = stackalloc byte[4];
        if (!Convert.TryFromBase64String(raw, bytes, out var written) || written != 4)
        {
            return false;
        }
        version = BitConverter.ToUInt32(bytes);
        return true;
    }
}
