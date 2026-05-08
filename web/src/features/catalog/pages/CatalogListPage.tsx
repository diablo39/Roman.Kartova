import { useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { Checkbox } from "@/components/base/checkbox/checkbox";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";

const ALLOWED_SORT_FIELDS = ["createdAt", "name"] as const;
const BOOLEAN_FILTERS = ["includeDecommissioned"] as const;

export function CatalogListPage() {
  const { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter } = useListUrlState({
    defaultSortBy: "createdAt",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    booleanFilters: BOOLEAN_FILTERS,
  });
  const includeDecommissioned = booleanFilters.includeDecommissioned;

  const list = useApplicationsList({ sortBy, sortOrder, includeDecommissioned });
  const [dialogOpen, setDialogOpen] = useState(false);

  useEffect(() => {
    if (list.isError) {
      console.error("CatalogListPage list error", list.error);
    }
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Catalog</h2>
        <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
          Register Application
        </Button>
      </div>

      <div className="flex items-center justify-end">
        <Checkbox
          isSelected={includeDecommissioned}
          onChange={(value: boolean) => setBooleanFilter("includeDecommissioned", value)}
          label="Show decommissioned"
        />
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          list={list}
          sortBy={sortBy}
          sortOrder={sortOrder}
          onSortChange={setSort}
        />
      )}

      <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  );
}
