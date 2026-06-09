using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRoles
{
    public const string PlatformAdmin = "platform-admin";
    public const string OrgAdmin = "OrgAdmin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
    public const string ServiceAccount = "ServiceAccount"; // forward-compat — no realm role yet (ADR-0009)

    /// <summary>
    /// Tenant-scoped roles that can be assigned through the invitation flow.
    /// Excludes <see cref="PlatformAdmin"/> (orthogonal to tenants) and
    /// <see cref="ServiceAccount"/> (no realm role yet — ADR-0009). Mirrors
    /// the three roles in <see cref="KartovaRolePermissions.Map"/>:
    /// Viewer, Member, OrgAdmin.
    /// </summary>
    public static readonly FrozenSet<string> All =
        new[] { Viewer, Member, OrgAdmin }.ToFrozenSet(StringComparer.Ordinal);
}
