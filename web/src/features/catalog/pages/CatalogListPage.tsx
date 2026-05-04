import { useState } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useApplications } from "@/features/catalog/api/applications";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";

export function CatalogListPage() {
  const query = useApplications();
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Catalog</h2>
        <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
          Register Application
        </Button>
      </div>

      {query.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-2 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">Try again in a moment, or check that you're signed in.</p>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          isLoading={query.isLoading}
          applications={query.data as never}
        />
      )}

      <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  );
}
