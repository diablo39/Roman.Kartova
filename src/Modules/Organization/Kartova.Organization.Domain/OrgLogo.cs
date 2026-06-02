using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Kartova.Organization.Domain;

public sealed class OrgLogo
{
    private static readonly FrozenSet<string> AcceptedMimeTypes =
        new[] { "image/png", "image/jpeg", "image/svg+xml" }.ToFrozenSet();

    private byte[] _bytes = [];

    /// <summary>
    /// Defensive clone preserves the ContentHash invariant — callers cannot
    /// mutate the stored bytes by writing into the returned array. EF
    /// materialization writes through the private setter (no double-clone);
    /// production reads (e.g., LogoCommands.GetServeDataAsync) allocate one
    /// fresh copy per serve, bounded by the 256 KiB logo size cap.
    /// </summary>
    [SuppressMessage(
        "Performance", "CA1819",
        Justification = "Defensive clone preserves ContentHash invariant on the OrgLogo aggregate; allocation cost is one per serve which is acceptable for the slice-9 logo size cap of 256 KiB.")]
    public byte[] Bytes
    {
        get => (byte[])_bytes.Clone();
        private set => _bytes = value;
    }

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

        var stored = (byte[])bytes.Clone();
        return new OrgLogo
        {
            Bytes = stored,    // setter assigns to _bytes directly — no extra clone
            MimeType = mimeType,
            ContentHash = Convert.ToHexString(SHA256.HashData(stored)),
        };
    }
}
