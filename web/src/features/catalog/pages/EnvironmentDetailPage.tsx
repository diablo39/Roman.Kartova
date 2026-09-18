import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useEnvironment } from "@/features/catalog/api/environments";
import { EnvironmentTypeBadge } from "@/features/catalog/components/EnvironmentTable";
import { asProblemDetails } from "@/shared/forms/problemDetails";

/**
 * Read-only environment detail (E-02.F-05.S-01, A1). Mirrors VmDetailPage's
 * loading/not-found shape but has no edit/delete actions and no relationship
 * sections yet — Environments have neither a team nor system-membership /
 * hosted-component edges in this slice.
 */
export function EnvironmentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useEnvironment(id ?? "");

  if (query.isLoading) {
    return (
      <Card data-testid="environment-detail-skeleton">
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
    const problem = asProblemDetails(query.error);
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">Environment not found</p>
          <p className="text-sm text-tertiary">
            {problem?.detail ??
              problem?.title ??
              "It may have been deleted, or you may not have access in this tenant."}
          </p>
        </CardContent>
      </Card>
    );
  }

  const env = query.data;
  const resourceEntries = Object.entries(env.resourceDetails ?? {});

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{env.displayName}</h2>
          <EnvironmentTypeBadge type={env.type} size="md" />
        </div>
      </div>
      <Card>
        <CardContent className="space-y-6 p-6">
          <section>
            <h3 className="text-sm font-medium text-tertiary">Description</h3>
            <p className="mt-1 text-sm text-secondary">
              {env.description ? env.description : <span className="italic">No description</span>}
            </p>
          </section>
          <hr className="border-secondary" />
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="ID" value={env.id} mono />
            <Field label="Region" value={env.region ?? "—"} />
            <Field label="Cluster" value={env.cluster ?? "—"} />
            <div>
              <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
              <div className="mt-1 text-sm">
                <Link to={`/users/${env.createdByUserId}`} className="font-medium text-primary hover:underline">
                  {env.createdByUserId}
                </Link>
              </div>
            </div>
            <Field label="Created" value={env.createdAt ? new Date(env.createdAt).toLocaleString() : "—"} />
          </section>
          <hr className="border-secondary" />
          <section>
            <h3 className="text-sm font-medium text-tertiary">Resource details</h3>
            {resourceEntries.length === 0 ? (
              <p className="mt-1 text-sm text-tertiary italic">No resource details recorded</p>
            ) : (
              <dl className="mt-2 grid grid-cols-1 gap-4 sm:grid-cols-3">
                {resourceEntries.map(([key, value]) => (
                  <div key={key}>
                    <dt className="text-xs uppercase tracking-wide text-tertiary">{key}</dt>
                    <dd className="mt-1 text-sm text-primary">{value}</dd>
                  </div>
                ))}
              </dl>
            )}
          </section>
        </CardContent>
      </Card>
    </div>
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
