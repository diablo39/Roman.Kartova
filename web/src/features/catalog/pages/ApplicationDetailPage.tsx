import { useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Badge } from "@/components/base/badges/badges";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useApplication } from "@/features/catalog/api/applications";

export function ApplicationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useApplication(id ?? "");

  if (query.isLoading) {
    return (
      <Card data-testid="detail-skeleton">
        <CardHeader>
          <Skeleton className="h-7 w-64" />
          <Skeleton className="mt-2 h-4 w-32" />
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-12 w-2/3" />
        </CardContent>
      </Card>
    );
  }

  if (query.isError || !query.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">Application not found</p>
          <p className="text-sm text-tertiary">
            It may have been deleted, or you may not have access in this tenant.
          </p>
        </CardContent>
      </Card>
    );
  }

  const app = query.data as {
    id: string;
    tenantId?: string;
    name: string;
    displayName: string;
    description: string;
    ownerUserId?: string;
    createdAt?: string;
  };

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-baseline gap-3">
          <h2 className="text-2xl font-semibold text-primary">{app.displayName}</h2>
          <Badge color="gray" type="pill-color" size="sm" className="font-mono">{app.name}</Badge>
          <Badge color="success" type="pill-color" size="sm">Active</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        <section>
          <h3 className="text-sm font-medium text-tertiary">Description</h3>
          <p className="mt-1 text-sm text-secondary">
            {app.description ? app.description : <span className="italic">No description</span>}
          </p>
        </section>
        <hr className="border-secondary" />
        <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="ID" value={app.id} mono />
          <Field label="Owner" value={app.ownerUserId ?? "—"} mono />
          <Field label="Created" value={app.createdAt ?? "—"} />
        </section>
      </CardContent>
    </Card>
  );
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-tertiary">{label}</div>
      <div className={mono ? "mt-1 font-mono text-sm text-primary" : "mt-1 text-sm text-primary"}>{value}</div>
    </div>
  );
}
