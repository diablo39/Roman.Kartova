using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRolePermissions
{
    private static readonly FrozenSet<string> EmptySet = FrozenSet<string>.Empty;

    public static readonly FrozenDictionary<string, FrozenSet<string>> Map =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            [KartovaRoles.Viewer] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.TeamRead,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgUsersRead,
            }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.Member] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogServicesRegister,
                KartovaPermissions.TeamRead,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgUsersRead,
                KartovaPermissions.OrgUsersSearch,
            }.ToFrozenSet(StringComparer.Ordinal),
            // OrgAdmin's authority on teams comes from the IsInRole(OrgAdmin) bypass in
            // TeamAdminOfThisHandler, not from team-mutation claims (ADR-0101).
            [KartovaRoles.OrgAdmin] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
                KartovaPermissions.CatalogServicesRegister,
                KartovaPermissions.TeamRead,
                KartovaPermissions.TeamCreate,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgProfileEdit,
                KartovaPermissions.OrgInvitationsRead,
                KartovaPermissions.OrgInvitationsCreate,
                KartovaPermissions.OrgInvitationsRevoke,
                KartovaPermissions.OrgUsersRead,
                KartovaPermissions.OrgUsersSearch,
                KartovaPermissions.OrgUsersRoleChange, KartovaPermissions.OrgUsersRemove,
            }.ToFrozenSet(StringComparer.Ordinal),
            // PlatformAdmin: orthogonal — operates outside tenant scope. No entry.
            // ServiceAccount: no realm role yet (ADR-0009). No entry.
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;
}
