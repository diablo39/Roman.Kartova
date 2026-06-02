import snapshot from "./permissions.snapshot.json";

export const KartovaPermissions = {
  CatalogRead: "catalog.read",
  CatalogApplicationsRegister: "catalog.applications.register",
  CatalogApplicationsEditMetadata: "catalog.applications.edit-metadata",
  CatalogApplicationsLifecycleForward: "catalog.applications.lifecycle.forward",
  CatalogApplicationsLifecycleReverse: "catalog.applications.lifecycle.reverse",
  TeamRead: "team.read",
  TeamCreate: "team.create",
  TeamMetadataEdit: "team.metadata.edit",
  TeamDelete: "team.delete",
  TeamMembersManage: "team.members.manage",

  OrgProfileRead: "org.profile.read",
  OrgProfileEdit: "org.profile.edit",
  OrgInvitationsRead: "org.invitations.read",
  OrgInvitationsCreate: "org.invitations.create",
  OrgInvitationsRevoke: "org.invitations.revoke",
  OrgUsersRead: "org.users.read",
  OrgUsersSearch: "org.users.search",
} as const;

export type KartovaPermission = (typeof KartovaPermissions)[keyof typeof KartovaPermissions];

// Drift guard against the committed snapshot — fails fast if the TS constants object
// diverges from the JSON shipped alongside it. The C# arch test
// `Ts_snapshot_equals_csharp_KartovaPermissions_All` guards the C#→snapshot side.
const declared = new Set(Object.values(KartovaPermissions));
const fromSnapshot = new Set(snapshot as readonly string[]);
if (declared.size !== fromSnapshot.size || [...declared].some((p) => !fromSnapshot.has(p))) {
  throw new Error(
    "KartovaPermissions constants drifted from permissions.snapshot.json. " +
      "If you added a permission in C#, regenerate the snapshot and update both.",
  );
}
