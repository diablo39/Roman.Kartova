import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useTeamsList } from "@/features/teams/api/teams";
import { CreateTeamDialog } from "@/features/teams/components/CreateTeamDialog";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const FILTER_SPECS: FilterSpec[] = [
  { key: "displayNameContains", type: "text", label: "Search teams", placeholder: "Search by name…" },
];

export function TeamsListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useTeamsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.queryFilters.displayNameContains as string | undefined,
  });
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

      <FilterBar specs={FILTER_SPECS} filters={filters} />

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
            <p className="text-base font-medium text-primary">
              {filters.isActive ? "No teams match your search" : "No teams yet"}
            </p>
            <p className="text-sm text-tertiary">
              {filters.isActive ? "Try a different name." : "Create your first team."}
            </p>
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
