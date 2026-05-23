using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRolePermissions
{
    private static readonly FrozenSet<string> EmptySet = FrozenSet<string>.Empty;

    public static readonly FrozenDictionary<string, FrozenSet<string>> Map =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            [KartovaRoles.Viewer] = new[] { KartovaPermissions.CatalogRead }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.Member] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.TeamAdmin] = new[]
            {
                // Forward-compat: same set as Member. Diverges when teams ship (E-03.F-02).
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.OrgAdmin] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
            }.ToFrozenSet(StringComparer.Ordinal),
            // PlatformAdmin: orthogonal — operates outside tenant scope. No entry.
            // ServiceAccount: no realm role yet (ADR-0009). No entry.
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;
}
