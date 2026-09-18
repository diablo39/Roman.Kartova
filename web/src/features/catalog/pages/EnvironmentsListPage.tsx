import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useEnvironmentsList } from "@/features/catalog/api/environments";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { EnvironmentTable } from "@/features/catalog/components/EnvironmentTable";
import { RegisterEnvironmentDialog } from "@/features/catalog/components/RegisterEnvironmentDialog";
import { environmentTypes } from "@/features/catalog/schemas/registerEnvironment";
import { environmentTypeLabel } from "@/features/catalog/components/EnvironmentTable";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { asProblemDetails } from "@/shared/forms/problemDetails";

// Wire names per EnvironmentSortField (Kartova.Catalog.Contracts) — camelCase, ADR-0095. Must
// stay in sync with EnvironmentTable's own SORT_FIELDS (its SortableHead ids drive onSortChange).
const ALLOWED_SORT_FIELDS = ["displayName", "createdAt", "type", "region"] as const;
const TEXT_FILTERS = ["region", "displayNameContains"] as const;
const MULTI_FILTERS = ["type"] as const;
const TYPE_OPTIONS = environmentTypes.map((t) => ({ label: environmentTypeLabel(t), value: t }));

export function EnvironmentsListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
    multiFilters: MULTI_FILTERS,
  });

  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "displayNameContains", type: "text", label: "Search environments", placeholder: "Search by name…" },
      { key: "type", type: "multi-select", label: "Type", placeholder: "All types", options: TYPE_OPTIONS },
      { key: "region", type: "text", label: "Region", placeholder: "Exact region…" },
    ],
    [],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useEnvironmentsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    type: filters.multiValues.type,
    region: filters.textValues.region,
    displayNameContains: filters.textValues.displayNameContains,
  });

  // Surface the server's ProblemDetails instead of the generic "Failed to load…" copy when
  // one is present (mirrors VirtualMachinesListPage/ServicesListPage).
  const errorDetail = useMemo(() => {
    if (!list.isError) return undefined;
    const problem = asProblemDetails(list.error);
    return problem?.detail ?? problem?.title;
  }, [list.isError, list.error]);

  // Clears every filter this page defines (mirrors FilterBar's own "Clear all").
  const clearFilters = () => {
    const text: Record<string, string> = {};
    const multi: Record<string, string[]> = {};
    for (const s of filterSpecs) {
      if (s.type === "text" || s.type === "single-select") text[s.key] = "";
      else if (s.type === "multi-select") multi[s.key] = [];
    }
    urlState.setFilters({ text, multi });
  };

  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogEnvironmentsRegister);

  useEffect(() => {
    if (list.isError) console.error("EnvironmentsListPage list error", list.error);
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Environments</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Environment
          </Button>
        )}
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load environments</p>
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
            <p className="text-base font-medium text-primary">No environments match your filters</p>
            <p className="text-sm text-tertiary">Try a different value or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <EnvironmentTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
        />
      )}

      {canRegister && <RegisterEnvironmentDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
