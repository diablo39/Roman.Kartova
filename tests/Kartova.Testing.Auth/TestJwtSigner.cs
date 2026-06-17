using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.IdentityModel.Tokens;

namespace Kartova.Testing.Auth;

[ExcludeFromCodeCoverage]
public sealed class TestJwtSigner
{
    public const string Issuer = "https://test-issuer.kartova.local";
    public const string Audience = "kartova-api";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;

    public TestJwtSigner()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-signing-key" };
    }

    public SecurityKey PublicKey => _key;

    public string IssueForTenant(
        TenantId tenantId,
        string[] roles,
        TimeSpan? lifetime = null,
        string subject = "test-user",
        string? email = null,
        string? name = null)
        => Build(subject, tenantId, roles, lifetime ?? TimeSpan.FromMinutes(15), expired: false, email: email, name: name);

    public string IssueForPlatformAdmin(string[]? extraRoles = null, string subject = "platform-admin-user")
    {
        var roles = new[] { KartovaRoles.PlatformAdmin }.Concat(extraRoles ?? []).ToArray();
        return Build(subject, tenantId: null, roles, TimeSpan.FromMinutes(15), expired: false, email: null);
    }

    public string IssueExpired(TenantId tenantId)
        => Build("test-user", tenantId, [KartovaRoles.OrgAdmin], TimeSpan.FromMinutes(15), expired: true, email: null);

    private string Build(
        string subject, TenantId? tenantId, string[] roles, TimeSpan lifetime, bool expired, string? email, string? name = null)
    {
        var now = expired ? DateTime.UtcNow.AddMinutes(-30) : DateTime.UtcNow;
        var expires = now.Add(lifetime);

        var realmAccess = JsonSerializer.Serialize(new { roles });
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(KartovaClaims.RealmAccess, realmAccess, JsonClaimValueTypes.Json),
        };
        if (tenantId is { } tid)
        {
            claims.Add(new Claim(KartovaClaims.TenantId, tid.Value.ToString()));
        }
        // Slice 9 / H1 batch 4: optional "email" claim plumbing — needed because
        // SessionStartHandler throws when the email claim is missing (it is now
        // a required bootstrap input for the users-projection upsert). Off by
        // default to preserve the wire shape every existing test was minted
        // against; opt-in by passing email through IssueForTenant or
        // CreateAuthenticatedClientAsync's emailClaim parameter.
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim("email", email));
        }
        if (!string.IsNullOrEmpty(name))
        {
            claims.Add(new Claim("name", name));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
