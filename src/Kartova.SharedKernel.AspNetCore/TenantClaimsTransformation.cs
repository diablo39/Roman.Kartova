using System.Security.Claims;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// IClaimsTransformation that reads <c>tenant_id</c> and realm roles from the validated JWT
/// and populates the scoped <see cref="ITenantContext"/>.
/// Realm roles live in the JSON claim <c>realm_access</c> with shape <c>{"roles": [...]}</c>.
/// </summary>
public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceProvider _services;

    public TenantClaimsTransformation(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return principal!;
        }

        var context = _services.GetRequiredService<ITenantContext>();
        context.Clear();

        var tenantIdClaim = principal.FindFirst(KartovaClaims.TenantId)?.Value;
        var tenantId = Multitenancy.TenantId.Empty;
        if (Multitenancy.TenantId.TryParse(tenantIdClaim, out var parsed))
        {
            tenantId = parsed;
        }

        var roles = ExtractRealmRoles(principal);
        context.Populate(tenantId, roles);

        if (roles.Count > 0 && principal.Identity is ClaimsIdentity id)
        {
            foreach (var role in roles)
            {
                if (!id.HasClaim(ClaimTypes.Role, role))
                {
                    id.AddClaim(new Claim(ClaimTypes.Role, role));
                }

                foreach (var perm in KartovaRolePermissions.ForRole(role))
                {
                    if (!id.HasClaim(KartovaClaims.Permission, perm))
                    {
                        id.AddClaim(new Claim(KartovaClaims.Permission, perm));
                    }
                }
            }
        }

        // Post-auth hooks: per-module sync work (user-projection upsert, invitation
        // acceptance, etc.) that needs to run inside the request scope AFTER the
        // tenant + role claims have been populated. Resolved from the current
        // scope; foreach is a no-op when no hooks are registered.
        foreach (var hook in _services.GetServices<IPostAuthSyncHook>())
        {
            await hook.ExecuteAsync(principal, CancellationToken.None);
        }

        return principal;
    }

    private static IReadOnlyCollection<string> ExtractRealmRoles(ClaimsPrincipal principal)
    {
        var realmAccess = principal.FindFirst(KartovaClaims.RealmAccess)?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var rolesElement) &&
                rolesElement.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>(rolesElement.GetArrayLength());
                foreach (var r in rolesElement.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String)
                    {
                        var v = r.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) result.Add(v);
                    }
                }
                return result;
            }
        }
        catch (JsonException) { /* malformed claim — ignore */ }

        return Array.Empty<string>();
    }
}
