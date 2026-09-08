import { useMemo, useEffect } from "react";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useVmList } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { VmTable } from "@/features/catalog/components/VmTable";
import { asProblemDetails } from "@/shared/forms/problemDetails";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["powerState", "os", "region", "hostname", "ipAddress"] as const;
const POWER_STATE_OPTIONS = [
  { label: "Running", value: "running" },
  { label: "Stopped", value: "stopped" },
  { label: "Suspended", value: "suspended" },
];

export function VirtualMachinesListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  // FILTER_SPECS: powerState is a single-select (the backend accepts one value, not a
  // multi-select — verified against the generated ListVms query shape); os/region/
  // hostname/ipAddress are free-text (ipAddress is a containment match server-side).
  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "powerState", type: "single-select", label: "Power state", options: POWER_STATE_OPTIONS },
      { key: "os", type: "text", label: "OS", placeholder: "Search by OS…" },
      { key: "region", type: "text", label: "Region", placeholder: "Search by region…" },
      { key: "hostname", type: "text", label: "Hostname", placeholder: "Search by hostname…" },
      { key: "ipAddress", type: "text", label: "IP address", placeholder: "Search by IP…" },
    ],
    [],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useVmList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    powerState: filters.textValues.powerState,
    os: filters.textValues.os,
    region: filters.textValues.region,
    hostname: filters.textValues.hostname,
    ipAddress: filters.textValues.ipAddress,
  });

  // Surface the server's ProblemDetails (e.g. too-many-filter-values) instead of the generic
  // "Failed to load…" copy when one is present (mirrors ServicesListPage).
  const errorDetail = useMemo(() => {
    if (!list.isError) return undefined;
    const problem = asProblemDetails(list.error);
    return problem?.detail ?? problem?.title;
  }, [list.isError, list.error]);

  // Clears every filter this page defines (mirrors FilterBar's own "Clear all").
  const clearFilters = () => {
    const text: Record<string, string> = {};
    for (const s of filterSpecs) {
      if (s.type === "text" || s.type === "single-select") text[s.key] = "";
    }
    urlState.setFilters({ text });
  };

  // TODO(Task 20): add a "+ Register VM" header button gated on
  // usePermissions().hasPermission(KartovaPermissions.CatalogInfrastructureRegister),
  // wired to RegisterVmDialog once that component exists. Omitted here rather than
  // importing a not-yet-created component, to keep this task's build green.

  useEffect(() => {
    if (list.isError) console.error("VirtualMachinesListPage list error", list.error);
  }, [list.isError, list.error]);

  useEffect(() => {
    if (teamsList.isError) console.error("VirtualMachinesListPage teams error", teamsList.error);
  }, [teamsList.isError, teamsList.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Virtual Machines</h2>
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load virtual machines</p>
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
            <p className="text-base font-medium text-primary">No virtual machines match your filters</p>
            <p className="text-sm text-tertiary">Try a different value or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <VmTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}
    </div>
  );
}
