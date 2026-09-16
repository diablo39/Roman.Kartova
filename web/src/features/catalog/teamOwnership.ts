/**
 * OrgAdmin, or a member of the team that owns the component — the ownership predicate shared
 * by gates like `canDeployOnVm` on ApplicationDetailPage/ServiceDetailPage. `teamId` is nullable
 * because some components (e.g. an unassigned Application) may have no owning team yet, in which
 * case only OrgAdmin passes. Intentionally NOT used for `canManageSuccessor` (ADR-0110's own gate
 * combines this ownership check with an additional lifecycle-forward permission) — left as-is.
 */
export function isOwningTeamMemberOrAdmin(
  role: string | null,
  teamIds: string[],
  teamId: string | null | undefined,
): boolean {
  return role === "OrgAdmin" || (teamId != null && teamIds.includes(teamId));
}
