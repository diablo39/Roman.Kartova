using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaPermissions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogApplicationsRegister = "catalog.applications.register";
    public const string CatalogApplicationsEditMetadata = "catalog.applications.edit-metadata";
    public const string CatalogApplicationsLifecycleForward = "catalog.applications.lifecycle.forward";
    public const string CatalogApplicationsLifecycleReverse = "catalog.applications.lifecycle.reverse";
    public const string CatalogServicesRegister = "catalog.services.register";
    public const string CatalogRelationshipsWrite = "catalog.relationships.write";

    public const string TeamRead          = "team.read";
    public const string TeamCreate        = "team.create";

    public const string OrgProfileRead         = "org.profile.read";
    public const string OrgProfileEdit         = "org.profile.edit";
    public const string OrgInvitationsRead     = "org.invitations.read";
    public const string OrgInvitationsCreate   = "org.invitations.create";
    public const string OrgInvitationsRevoke   = "org.invitations.revoke";
    public const string OrgUsersRead           = "org.users.read";
    public const string OrgUsersSearch         = "org.users.search";
    public const string OrgUsersRoleChange     = "org.users.role.change";
    public const string OrgUsersRemove         = "org.users.remove";

    public static FrozenSet<string> All { get; } = new[]
    {
        CatalogRead,
        CatalogApplicationsRegister,
        CatalogApplicationsEditMetadata,
        CatalogApplicationsLifecycleForward,
        CatalogApplicationsLifecycleReverse,
        CatalogServicesRegister,
        CatalogRelationshipsWrite,
        TeamRead,
        TeamCreate,
        OrgProfileRead,
        OrgProfileEdit,
        OrgInvitationsRead,
        OrgInvitationsCreate,
        OrgInvitationsRevoke,
        OrgUsersRead, OrgUsersSearch, OrgUsersRoleChange, OrgUsersRemove,
    }.ToFrozenSet(StringComparer.Ordinal);
}
