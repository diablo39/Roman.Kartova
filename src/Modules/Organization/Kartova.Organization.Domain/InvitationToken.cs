using System.Buffers.Text;
using System.Security.Cryptography;

namespace Kartova.Organization.Domain;

/// <summary>
/// Opaque single-use invitation token. The plaintext is delivered to the
/// invitee (copy-link URL); only <see cref="Hash"/> is ever persisted. 256-bit
/// CSPRNG entropy via <see cref="RandomNumberGenerator"/> — NOT a GUID, which
/// is neither contractually cryptographic nor safe to expose as a credential.
/// </summary>
public static class InvitationToken
{
    /// <summary>Generates a fresh (plaintext, hash) pair. Plaintext goes in the URL; hash is stored.</summary>
    public static (string Plaintext, string Hash) Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Base64Url.EncodeToString(bytes);
        return (plaintext, Hash(plaintext));
    }

    /// <summary>Deterministic base64url(SHA-256(plaintext)) — used at issuance and validation.</summary>
    public static string Hash(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        return Base64Url.EncodeToString(digest);
    }
}
