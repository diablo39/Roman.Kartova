import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { DetailTabs } from "@/components/application/tabs/detail-tabs";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { useSystem } from "@/features/catalog/api/systems";
import { useTeamsList } from "@/features/teams/api/teams";
import { SystemMembersSection } from "@/features/catalog/components/SystemMembersSection";

export function SystemDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useSystem(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="system-detail-skeleton">
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
          <p className="text-base font-medium text-error-primary">System not found</p>
          <p className="text-sm text-tertiary">It may have been deleted, or you may not have access in this tenant.</p>
        </CardContent>
      </Card>
    );
  }

  const sys = query.data;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-2xl font-semibold text-primary">{sys.displayName}</h2>
      </div>
      <Card>
        <DetailTabs aria-label={sys.displayName}>
          <DetailTabs.Tab id="overview" label="Overview">
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-tertiary">Description</h3>
                <p className="mt-1 text-sm text-secondary">
                  {sys.description ? sys.description : <span className="italic">No description</span>}
                </p>
              </section>
              <hr className="border-secondary" />
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="ID" value={sys.id} mono />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Steward team</div>
                  <div className="mt-1 text-sm">
                    <Link to={`/teams/${sys.teamId}`} className="text-primary hover:underline">
                      {teamNameById.get(sys.teamId) ?? "View team"}
                    </Link>
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                  <div className="mt-1 text-sm"><CreatedByLink user={sys.createdBy} /></div>
                </div>
                <Field label="Created" value={sys.createdAt ? new Date(sys.createdAt).toLocaleString() : "—"} />
              </section>
            </div>
          </DetailTabs.Tab>

          <DetailTabs.Tab id="members" label="Members">
            <SystemMembersSection systemId={sys.id} />
          </DetailTabs.Tab>
        </DetailTabs>
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
