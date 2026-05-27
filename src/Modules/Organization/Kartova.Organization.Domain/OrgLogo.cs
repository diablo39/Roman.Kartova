using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Kartova.Organization.Domain;

public sealed class OrgLogo
{
    private static readonly FrozenSet<string> AcceptedMimeTypes =
        new[] { "image/png", "image/jpeg", "image/svg+xml" }.ToFrozenSet();

    public byte[] Bytes { get; private set; } = [];
    public string MimeType { get; private set; } = "";
    public string ContentHash { get; private set; } = "";

    [SuppressMessage("Performance", "CA1822", Justification = "EF requires instance ctor.")]
    private OrgLogo() { }

    public static OrgLogo Create(byte[] bytes, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.Length > 256 * 1024)
            throw new ArgumentException("Logo bytes must be 1..262144.", nameof(bytes));
        if (!AcceptedMimeTypes.Contains(mimeType))
            throw new ArgumentException("Unsupported logo mime-type.", nameof(mimeType));
        return new OrgLogo
        {
            Bytes = bytes,
            MimeType = mimeType,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }
}
