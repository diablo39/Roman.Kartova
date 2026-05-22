namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRolePermissions
{
    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [KartovaRoles.Viewer] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
            },
            [KartovaRoles.Member] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.TeamAdmin] = new HashSet<string>(StringComparer.Ordinal)
            {
                // Forward-compat in slice 7: same set as Member. Diverges when teams ship (E-03.F-02).
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.OrgAdmin] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
            },
            // PlatformAdmin: orthogonal — operates outside tenant scope. No entry.
            // ServiceAccount: no realm role yet (ADR-0009). No entry.
        };

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;
}
