import { lazy, Suspense, useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Table } from "@/components/application/table/table";
import { HealthBadge } from "@/features/catalog/components/HealthBadge";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { useService } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { PROTOCOL_LABEL } from "@/features/catalog/schemas/registerService";
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";

const DependencyMiniGraph = lazy(() =>
  import("@/features/catalog/components/DependencyMiniGraph").then((m) => ({ default: m.DependencyMiniGraph })),
);

export function ServiceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useService(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="service-detail-skeleton">
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
          <p className="text-base font-medium text-error-primary">Service not found</p>
          <p className="text-sm text-tertiary">
            It may have been deleted, or you may not have access in this tenant.
          </p>
        </CardContent>
      </Card>
    );
  }

  const svc = query.data;

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{svc.displayName}</h2>
          <HealthBadge health={svc.health} size="md" />
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        <section>
          <h3 className="text-sm font-medium text-tertiary">Description</h3>
          <p className="mt-1 text-sm text-secondary">
            {svc.description ? svc.description : <span className="italic">No description</span>}
          </p>
        </section>

        <hr className="border-secondary" />

        <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="ID" value={svc.id} mono />
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
            <div className="mt-1 text-sm">
              <Link to={`/teams/${svc.teamId}`} className="text-primary hover:underline">
                {teamNameById.get(svc.teamId) ?? "View team"}
              </Link>
            </div>
          </div>
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
            <div className="mt-1 text-sm">
              <CreatedByLink user={svc.createdBy} />
            </div>
          </div>
          <Field label="Created" value={svc.createdAt ? new Date(svc.createdAt).toLocaleString() : "—"} />
          <Field label="Version" value={svc.version} mono />
        </section>

        <hr className="border-secondary" />

        <section>
          <h3 className="text-sm font-medium text-tertiary">Endpoints</h3>
          {svc.endpoints.length === 0 ? (
            <p className="mt-1 text-sm text-tertiary italic">No endpoints registered</p>
          ) : (
            <div className="mt-2 overflow-hidden rounded-lg ring-1 ring-secondary">
              <Table aria-label="Service endpoints">
                <Table.Header>
                  <Table.Head id="url" isRowHeader>URL</Table.Head>
                  <Table.Head id="protocol">Protocol</Table.Head>
                </Table.Header>
                <Table.Body>
                  {svc.endpoints.map((e, i) => (
                    <Table.Row key={`${e.url}-${i}`} id={`${e.url}-${i}`}>
                      <Table.Cell className="font-mono text-sm text-primary">{e.url}</Table.Cell>
                      <Table.Cell className="text-sm">{PROTOCOL_LABEL[e.protocol]}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table>
            </div>
          )}
        </section>
          <hr className="border-secondary" />
          <Suspense fallback={<Skeleton className="h-80 w-full" />}>
            <DependencyMiniGraph entityKind="service" entityId={svc.id} displayName={svc.displayName} />
          </Suspense>
          <hr className="border-secondary" />
          <RelationshipsSection
            entityKind="service"
            entityId={svc.id}
            entityTeamId={svc.teamId}
            entityDisplayName={svc.displayName}
          />
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
