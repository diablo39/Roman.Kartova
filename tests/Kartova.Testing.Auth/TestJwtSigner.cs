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

    public string IssueForTenant(TenantId tenantId, string[] roles, TimeSpan? lifetime = null, string subject = "test-user")
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(15));

        var realmAccess = JsonSerializer.Serialize(new { roles });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Iss, Issuer),
            new(JwtRegisteredClaimNames.Aud, Audience),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("tenant_id", tenantId.Value.ToString()),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var creds = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueForPlatformAdmin(string[]? extraRoles = null, string subject = "platform-admin-user")
    {
        var roles = new[] { "platform-admin" }.Concat(extraRoles ?? Array.Empty<string>()).ToArray();
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(15);
        var realmAccess = JsonSerializer.Serialize(new { roles });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Iss, Issuer),
            new(JwtRegisteredClaimNames.Aud, Audience),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueExpired(TenantId tenantId)
    {
        var now = DateTime.UtcNow.AddMinutes(-30);
        var expires = now.AddMinutes(15); // still in the past

        var realmAccess = JsonSerializer.Serialize(new { roles = new[] { "OrgAdmin" } });

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user"),
            new("tenant_id", tenantId.Value.ToString()),
            new("realm_access", realmAccess, JsonClaimValueTypes.Json),
        };

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
