using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRoles
{
    public const string PlatformAdmin = "platform-admin";
    public const string OrgAdmin = "OrgAdmin";
    public const string TeamAdmin = "TeamAdmin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
    public const string ServiceAccount = "ServiceAccount"; // forward-compat — no realm role yet (ADR-0009)

    /// <summary>
    /// Tenant-scoped roles that can be assigned through the invitation flow.
    /// Excludes <see cref="PlatformAdmin"/> (orthogonal to tenants) and
    /// <see cref="ServiceAccount"/> (no realm role yet — ADR-0009). Mirrors
    /// the four roles in <see cref="KartovaRolePermissions.Map"/>.
    /// </summary>
    public static readonly FrozenSet<string> All =
        new[] { Viewer, Member, TeamAdmin, OrgAdmin }.ToFrozenSet(StringComparer.Ordinal);
}
