import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useSystemsList } from "@/features/catalog/api/systems";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const MULTI_FILTERS = ["lifecycle", "teamId", "systemId"] as const;
const LIFECYCLE_OPTIONS = [
  { label: "Active", value: "active" },
  { label: "Deprecated", value: "deprecated" },
  { label: "Decommissioned", value: "decommissioned" },
];

export function CatalogListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
    multiFilters: MULTI_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map(t => [t.id, t.displayName])),
    [teamsList.items],
  );

  // Known limit: same 200-item cap as the team facet above — a tenant with more Systems gets a
  // filter that cannot express the rest. Accepted precedent (useTeamsList does this in 13 places).
  const systemsList = useSystemsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });

  // FILTER_SPECS is dynamic: team + system options come from their respective fetches. Lifecycle +
  // search are static. (Known limit: the team dropdown shows only the first 200
  // teams — same cap as the existing teamNameById lookup; see spec §2.)
  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "displayNameContains", type: "text", label: "Search applications", placeholder: "Search by name…" },
      { key: "lifecycle", type: "multi-select", label: "Lifecycle", placeholder: "Any status", options: LIFECYCLE_OPTIONS },
      {
        key: "teamId",
        type: "multi-select",
        label: "Team",
        placeholder: "All teams",
        options: (teamsList.items ?? []).map(t => ({ label: t.displayName, value: t.id })),
      },
      {
        key: "systemId",
        type: "multi-select",
        label: "System",
        placeholder: "All systems",
        options: (systemsList.items ?? []).map((s) => ({ label: s.displayName, value: s.id })),
      },
    ],
    [teamsList.items, systemsList.items],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useApplicationsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.textValues.displayNameContains,
    lifecycle: filters.multiValues.lifecycle,
    teamId: filters.multiValues.teamId,
    systemId: filters.multiValues.systemId,
  });

  // Surface the server's ProblemDetails (e.g. too-many-filter-values) instead of the generic
  // "Failed to load…" copy when one is present, so a filter-cap 400 is legible instead of a
  // dead end (gate 7/8 fix). Falls back to undefined for non-ProblemDetails errors (network
  // failures, etc.), which keep the generic copy.
  const errorDetail = useMemo(() => {
    if (!list.isError) return undefined;
    const problem = list.error as ProblemDetails | undefined;
    return problem?.detail ?? problem?.title;
  }, [list.isError, list.error]);

  // Clears every filter this page defines (mirrors FilterBar's own "Clear all" — reuses
  // urlState.setFilters rather than inventing a second URL-clearing path). Unlike list.reset()
  // (which only pops the cursor stack), this actually removes the offending params from the
  // URL, so a filter-cap 400 has a real escape instead of the Reset button replaying the same
  // request forever.
  const clearFilters = () => {
    const text: Record<string, string> = {};
    const booleans: Record<string, boolean> = {};
    const multi: Record<string, string[]> = {};
    for (const s of filterSpecs) {
      if (s.type === "text" || s.type === "single-select") text[s.key] = "";
      else if (s.type === "boolean") booleans[s.key] = false;
      else if (s.type === "multi-select") multi[s.key] = [];
    }
    urlState.setFilters({ text, booleans, multi });
  };

  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogApplicationsRegister);

  useEffect(() => {
    if (list.isError) console.error("CatalogListPage list error", list.error);
  }, [list.isError, list.error]);

  // The team filter's options come from useTeamsList; a failed fetch otherwise
  // renders an empty Team dropdown with no signal. Log it (mirrors the list error
  // above) so a broken team filter is observable.
  useEffect(() => {
    if (teamsList.isError) console.error("CatalogListPage teams error", teamsList.error);
  }, [teamsList.isError, teamsList.error]);

  // Same rationale as the team filter's error effect above — a failed useSystemsList fetch
  // otherwise renders an empty System dropdown with no signal.
  useEffect(() => {
    if (systemsList.isError) console.error("CatalogListPage systems error", systemsList.error);
  }, [systemsList.isError, systemsList.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Applications</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Application
          </Button>
        )}
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">{errorDetail ?? "Try refreshing or resetting the list."}</p>
            <div className="flex items-center justify-center gap-2">
              <Button size="sm" onClick={() => list.reset()}>Reset</Button>
              {filters.isActive && (
                <Button size="sm" color="secondary" onClick={clearFilters}>Clear filters</Button>
              )}
            </div>
          </CardContent>
        </Card>
      ) : !list.isLoading && list.items.length === 0 && filters.isActive ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No applications match your filters</p>
            <p className="text-sm text-tertiary">Try a different name or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
