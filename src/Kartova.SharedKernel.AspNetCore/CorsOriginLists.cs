namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Startup guards for the two CORS allow-lists. ADR-0118 requires the operator console to run on
/// its own origin, so an origin in both lists (or a wildcard admin list) would silently re-merge the
/// tenant and operator surfaces — fail fast instead.
/// </summary>
public static class CorsOriginLists
{
    /// <summary>
    /// Trims whitespace and a trailing <c>/</c> and drops empty entries. A browser <c>Origin</c> header
    /// never carries a trailing slash, so <c>https://admin.example/</c> in config would never match.
    /// </summary>
    public static string[] Normalize(IEnumerable<string> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        return origins
            .Select(o => (o ?? string.Empty).Trim().TrimEnd('/'))
            .Where(o => o.Length > 0)
            .ToArray();
    }

    public static void Validate(IReadOnlyCollection<string> tenantOrigins, IReadOnlyCollection<string> adminOrigins)
    {
        ArgumentNullException.ThrowIfNull(tenantOrigins);
        ArgumentNullException.ThrowIfNull(adminOrigins);

        var admin = Normalize(adminOrigins);
        if (admin.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{CorsConfigKeys.AdminAllowedOrigins} must not contain '*': the operator console must run on its own origin (ADR-0118).");
        }

        var tenant = Normalize(tenantOrigins).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shared = admin.Where(tenant.Contains).ToArray();
        if (shared.Length > 0)
        {
            throw new InvalidOperationException(
                $"Origins present in both {CorsConfigKeys.AllowedOrigins} and {CorsConfigKeys.AdminAllowedOrigins}: " +
                $"{string.Join(", ", shared)}. The operator console must run on its own origin (ADR-0118).");
        }
    }
}
