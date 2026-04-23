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

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal!);
        }

        var context = _services.GetRequiredService<ITenantContext>();
        context.Clear();

        var tenantIdClaim = principal.FindFirst("tenant_id")?.Value;
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
            }
        }

        return Task.FromResult(principal);
    }

    private static IReadOnlyCollection<string> ExtractRealmRoles(ClaimsPrincipal principal)
    {
        var realmAccess = principal.FindFirst("realm_access")?.Value;
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
