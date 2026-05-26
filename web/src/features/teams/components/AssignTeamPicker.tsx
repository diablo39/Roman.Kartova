import { toast } from "sonner";

import { useTeamsList } from "@/features/teams/api/teams";
import { useAssignApplicationTeam } from "@/features/catalog/api/applications";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

interface Props {
  applicationId: string;
  currentTeamId: string | null | undefined;
  /** If true, the user lacks edit-metadata permission overall. Pass through from parent. */
  disabled?: boolean;
}

export function AssignTeamPicker({ applicationId, currentTeamId, disabled = false }: Props) {
  const perms = usePermissions();
  const isOrgAdmin = perms.role === "OrgAdmin";
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const mutation = useAssignApplicationTeam(applicationId);

  const allTeams = teamsList.items ?? [];
  const visibleTeams = isOrgAdmin
    ? allTeams
    : allTeams.filter((t) => perms.teamIds.includes(t.id));

  const currentTeamName = currentTeamId
    ? allTeams.find((t) => t.id === currentTeamId)?.displayName ?? "Unknown team"
    : "Unassigned";

  const canEdit = !disabled && perms.hasPermission(KartovaPermissions.CatalogApplicationsEditMetadata);

  const onChange = async (value: string) => {
    const newTeamId = value === "__unassigned__" ? null : value;
    try {
      await mutation.mutateAsync(newTeamId);
      toast.success(newTeamId ? "Team assigned" : "Team unassigned");
    } catch (err) {
      const problem = err as { __status?: number; detail?: string };
      if (problem.__status === 422) {
        toast.error("Selected team is no longer available. Refresh and try again.");
        return;
      }
      if (problem.__status === 403) {
        toast.error("You don't have permission to move this application between these teams.");
        return;
      }
      toast.error(problem.detail ?? "Could not assign team");
    }
  };

  if (teamsList.isLoading) return <span className="text-sm text-tertiary">Team: loading...</span>;

  return (
    <div className="flex items-center gap-2 text-sm">
      <label htmlFor="assign-team" className="text-tertiary">Team:</label>
      <select
        id="assign-team"
        className="rounded-md border border-secondary px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60"
        value={currentTeamId ?? "__unassigned__"}
        onChange={(e) => void onChange(e.target.value)}
        disabled={!canEdit || mutation.isPending}
      >
        <option value="__unassigned__">Unassigned</option>
        {visibleTeams.map((t) => (
          <option key={t.id} value={t.id}>{t.displayName}</option>
        ))}
      </select>
      {!canEdit && <span className="text-tertiary">({currentTeamName})</span>}
    </div>
  );
}
