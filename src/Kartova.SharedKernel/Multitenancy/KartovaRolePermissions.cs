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
                KartovaPermissions.TeamRead,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgUsersRead,
                KartovaPermissions.OrgUsersSearch,
            }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.TeamAdmin] = new[]
            {
                // Diverges from Member: gains team metadata/delete/members permissions
                // (gated to own team via resource auth, ADR-0098 slice 8).
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.TeamRead,
                KartovaPermissions.TeamMetadataEdit,
                KartovaPermissions.TeamDelete,
                KartovaPermissions.TeamMembersManage,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgUsersRead,
                KartovaPermissions.OrgUsersSearch,
            }.ToFrozenSet(StringComparer.Ordinal),
            [KartovaRoles.OrgAdmin] = new[]
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
                KartovaPermissions.TeamRead,
                KartovaPermissions.TeamCreate,
                KartovaPermissions.TeamMetadataEdit,
                KartovaPermissions.TeamDelete,
                KartovaPermissions.TeamMembersManage,
                KartovaPermissions.OrgProfileRead,
                KartovaPermissions.OrgProfileEdit,
                KartovaPermissions.OrgInvitationsRead,
                KartovaPermissions.OrgInvitationsCreate,
                KartovaPermissions.OrgInvitationsRevoke,
                KartovaPermissions.OrgUsersRead,
                KartovaPermissions.OrgUsersSearch,
            }.ToFrozenSet(StringComparer.Ordinal),
            // PlatformAdmin: orthogonal — operates outside tenant scope. No entry.
            // ServiceAccount: no realm role yet (ADR-0009). No entry.
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;
}
