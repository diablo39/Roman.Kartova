import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useApplications } from "@/features/catalog/api/applications";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";

export function CatalogListPage() {
  const query = useApplications();
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold">Catalog</h2>
        <Button onClick={() => setDialogOpen(true)} size="sm">
          <Plus className="mr-1.5 h-4 w-4" />
          Register Application
        </Button>
      </div>

      {query.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-2 p-6 text-center">
            <p className="text-base font-medium text-destructive">Failed to load applications</p>
            <p className="text-sm text-muted-foreground">Try again in a moment, or check that you're signed in.</p>
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
