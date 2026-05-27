import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useTeamsList } from "@/features/teams/api/teams";
import { CreateTeamDialog } from "@/features/teams/components/CreateTeamDialog";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;

export function TeamsListPage() {
  const { sortBy, sortOrder } = useListUrlState({
    defaultSortBy: "createdAt",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });

  const list = useTeamsList({ sortBy, sortOrder });
  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canCreate = !permissionsLoading && hasPermission(KartovaPermissions.TeamCreate);

  useEffect(() => {
    if (list.isError) {
      console.error("TeamsListPage list error", list.error);
    }
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Teams</h2>
        {canCreate && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Create team
          </Button>
        )}
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load teams</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : list.isLoading ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="p-6 text-center text-sm text-tertiary">Loading…</CardContent>
        </Card>
      ) : list.items.length === 0 ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No teams yet</p>
            <p className="text-sm text-tertiary">Create your first team.</p>
          </CardContent>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
          <table className="w-full text-left text-sm">
            <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
              <tr>
                <th className="px-4 py-3 font-medium">Display name</th>
                <th className="px-4 py-3 font-medium">Description</th>
                <th className="px-4 py-3 font-medium">Created at</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-secondary">
              {list.items.map(team => (
                <tr key={team.id} className="hover:bg-primary_hover">
                  <td className="px-4 py-3">
                    <Link to={`/teams/${team.id}`} className="font-medium text-primary hover:underline">
                      {team.displayName}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-tertiary">
                    {team.description || <span className="italic">No description</span>}
                  </td>
                  <td className="px-4 py-3 text-tertiary">
                    {new Date(team.createdAt).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <CreateTeamDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  );
}
