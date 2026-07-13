import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { Badge } from "@/components/base/badges/badges";
import { useApi } from "@/features/catalog/api/apis";
import { useTeamsList } from "@/features/teams/api/teams";
import { API_STYLE_LABEL } from "@/features/catalog/schemas/registerApi";
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
import { ApiSpecSection } from "@/features/catalog/components/ApiSpecSection";
import { DetailTabs } from "@/components/application/tabs/detail-tabs";

export function ApiDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useApi(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="api-detail-skeleton">
        <CardHeader><Skeleton className="h-7 w-64" /><Skeleton className="mt-2 h-4 w-32" /></CardHeader>
        <CardContent className="space-y-4"><Skeleton className="h-20 w-full" /><Skeleton className="h-12 w-2/3" /></CardContent>
      </Card>
    );
  }

  if (query.isError || !query.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">API not found</p>
          <p className="text-sm text-tertiary">It may have been deleted, or you may not have access in this tenant.</p>
        </CardContent>
      </Card>
    );
  }

  const api = query.data;

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{api.displayName}</h2>
          <Badge type="pill-color" color="gray" size="md">{API_STYLE_LABEL[api.style]}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <DetailTabs aria-label={api.displayName}>
          <DetailTabs.Tab id="overview" label="Overview">
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-tertiary">Description</h3>
                <p className="mt-1 text-sm text-secondary">
                  {api.description ? api.description : <span className="italic">No description</span>}
                </p>
              </section>
              <hr className="border-secondary" />
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="ID" value={api.id} mono />
                <Field label="Version" value={api.version} mono />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
                  <div className="mt-1 text-sm">
                    <Link to={`/teams/${api.teamId}`} className="text-primary hover:underline">
                      {teamNameById.get(api.teamId) ?? "View team"}
                    </Link>
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                  <div className="mt-1 text-sm"><CreatedByLink user={api.createdBy} /></div>
                </div>
                <Field label="Created" value={api.createdAt ? new Date(api.createdAt).toLocaleString() : "—"} />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Spec</div>
                  <div className="mt-1 text-sm">
                    {api.specUrl ? (
                      <a href={api.specUrl} target="_blank" rel="noopener noreferrer" className="text-primary hover:underline break-all">
                        View spec
                      </a>
                    ) : (
                      <span className="text-tertiary italic">No spec URL</span>
                    )}
                  </div>
                </div>
              </section>
            </div>
          </DetailTabs.Tab>

          <DetailTabs.Tab id="dependencies" label="Dependencies">
            <RelationshipsSection
              entityKind="api"
              entityId={api.id}
              entityTeamId={api.teamId}
              entityDisplayName={api.displayName}
              variant="incoming-only"
            />
          </DetailTabs.Tab>

          <DetailTabs.Tab id="definition" label="Definition">
            <ApiSpecSection api={api} />
          </DetailTabs.Tab>
        </DetailTabs>
      </CardContent>
    </Card>
  );
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-tertiary">{label}</div>
      <div className={mono ? "mt-1 font-mono text-sm text-primary break-all" : "mt-1 text-sm text-primary"}>{value}</div>
    </div>
  );
}
