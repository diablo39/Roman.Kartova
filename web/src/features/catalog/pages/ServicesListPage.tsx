import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useServicesList } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ServicesTable } from "@/features/catalog/components/ServicesTable";
import { RegisterServiceDialog } from "@/features/catalog/components/RegisterServiceDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;

export function ServicesListPage() {
  const { sortBy, sortOrder, setSort } = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });

  const list = useServicesList({ sortBy, sortOrder });
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );
  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogServicesRegister);

  useEffect(() => {
    if (list.isError) console.error("ServicesListPage list error", list.error);
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Services</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Service
          </Button>
        )}
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load services</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : (
        <ServicesTable
          list={list}
          sortBy={sortBy}
          sortOrder={sortOrder}
          onSortChange={setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterServiceDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
