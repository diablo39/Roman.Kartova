using System.Security.Claims;
using System.Text.Json;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// <see cref="IClaimsTransformation"/> that reads <c>tenant_id</c> and realm roles from
/// the validated JWT and populates the scoped <see cref="ITenantContext"/>.
/// Realm roles live in the JSON claim <c>realm_access</c> with shape <c>{"roles": [...]}</c>.
/// <para>
/// This transformation does claims-only work: it MUST NOT touch any service that
/// depends on an active <see cref="ITenantScope"/>. ASP.NET runs
/// <see cref="IClaimsTransformation"/> implementations inside the
/// <c>UseAuthentication</c> middleware, which fires BEFORE
/// <see cref="TenantScopeBeginMiddleware"/> opens the per-request connection +
/// transaction. Resolving (e.g.) a module DbContext from here throws
/// "TenantScope is not active" because the DbContext options factory calls
/// <see cref="INpgsqlTenantScope.Connection"/> at materialization
/// (see <c>AddModuleDbContextExtensions</c>).
/// </para>
/// <para>
/// Post-auth sync work (user-projection upsert from JWT claims, invitation
/// acceptance, etc.) is performed inline by the Organization module's
/// <c>SessionStartHandler</c> on <c>POST /api/v1/auth/session</c> — the SPA's
/// <c>OidcCallbackHandler</c> always calls that endpoint first after the KC
/// roundtrip, so no separate pipeline hook is needed.
/// </para>
/// </summary>
public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceProvider _services;

    public TenantClaimsTransformation(IServiceProvider services)
    {
        _services = services;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal!);
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

        return Task.FromResult(principal);
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
