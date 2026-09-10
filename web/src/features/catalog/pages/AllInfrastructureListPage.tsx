import { useMemo, useEffect } from "react";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useInfrastructureList } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { useSystemsList } from "@/features/catalog/api/systems";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { AllInfrastructureTable } from "@/features/catalog/components/AllInfrastructureTable";
import { infraTypeLabel, type InfraType } from "@/features/catalog/infraType";
import { asProblemDetails } from "@/shared/forms/problemDetails";

// Wire names per InfrastructureSortField (Kartova.Catalog.Contracts) — camelCase, ADR-0095.
const ALLOWED_SORT_FIELDS = ["createdAt", "displayName", "provider"] as const;
const MULTI_FILTERS = ["type"] as const;

// Currently just VirtualMachine — `InfraType` is a total union so a second member added there
// would need to be added here too (or this could be derived, but the union has one member today).
const INFRA_TYPE_OPTIONS: { label: string; value: InfraType }[] = [
  { label: infraTypeLabel("virtualMachine"), value: "virtualMachine" },
];

export function AllInfrastructureListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    multiFilters: MULTI_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  // Known limit: same 200-item cap as the team facet above (precedent: ServicesListPage).
  const systemsList = useSystemsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const systemNameById = useMemo(
    () => new Map<string, string>((systemsList.items ?? []).map((s) => [s.id, s.displayName])),
    [systemsList.items],
  );

  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "type", type: "multi-select", label: "Type", placeholder: "All types", options: INFRA_TYPE_OPTIONS },
    ],
    [],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useInfrastructureList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    type: filters.multiValues.type,
  });

  const errorDetail = useMemo(() => {
    if (!list.isError) return undefined;
    const problem = asProblemDetails(list.error);
    return problem?.detail ?? problem?.title;
  }, [list.isError, list.error]);

  const clearFilters = () => {
    urlState.setFilters({ multi: { type: [] } });
  };

  useEffect(() => {
    if (list.isError) console.error("AllInfrastructureListPage list error", list.error);
  }, [list.isError, list.error]);

  useEffect(() => {
    if (teamsList.isError) console.error("AllInfrastructureListPage teams error", teamsList.error);
  }, [teamsList.isError, teamsList.error]);

  useEffect(() => {
    if (systemsList.isError) console.error("AllInfrastructureListPage systems error", systemsList.error);
  }, [systemsList.isError, systemsList.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Infrastructure</h2>
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load infrastructure</p>
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
            <p className="text-base font-medium text-primary">No infrastructure matches your filters</p>
            <p className="text-sm text-tertiary">Try a different type or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <AllInfrastructureTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
          systemNameById={systemNameById}
        />
      )}
    </div>
  );
}
