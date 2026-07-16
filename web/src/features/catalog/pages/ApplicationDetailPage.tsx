import { lazy, Suspense, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Button } from "@/components/base/buttons/button";
import { DetailTabs } from "@/components/application/tabs/detail-tabs";
import { useApplication } from "@/features/catalog/api/applications";
import { LifecycleMenu } from "@/features/catalog/components/LifecycleMenu";
import { EditApplicationDialog } from "@/features/catalog/components/EditApplicationDialog";
import { SetSuccessorDialog } from "@/features/catalog/components/SetSuccessorDialog";
import { AssignTeamPicker } from "@/features/teams/components/AssignTeamPicker";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
import { ApiSurfaceSection } from "@/features/catalog/components/ApiSurfaceSection";

const DependencyMiniGraph = lazy(() =>
  import("@/features/catalog/components/DependencyMiniGraph").then((m) => ({ default: m.DependencyMiniGraph })),
);

export function ApplicationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useApplication(id ?? "");
  const [editOpen, setEditOpen] = useState(false);
  const [successorDialogOpen, setSuccessorDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading, role, teamIds } = usePermissions();
  const canEditMetadata = hasPermission(KartovaPermissions.CatalogApplicationsEditMetadata);
  const canForwardLifecycle = hasPermission(KartovaPermissions.CatalogApplicationsLifecycleForward);
  const canReverseLifecycle = hasPermission(KartovaPermissions.CatalogApplicationsLifecycleReverse);
  const canOverrideSunset = hasPermission(KartovaPermissions.CatalogApplicationsLifecycleOverride);

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

  const app = query.data;
  // Defense-in-depth: hide Edit when terminal OR when user lacks the permission.
  // The server still returns 409 LifecycleConflict / 403 if a stale client tries anyway.
  const canEdit = !permissionsLoading && canEditMetadata && app.lifecycle !== "decommissioned";
  // ADR-0110: successor management gated on Deprecated + (OrgAdmin or team member) + lifecycle.forward.
  const canManageSuccessor =
    !permissionsLoading &&
    canForwardLifecycle &&
    (role === "OrgAdmin" || (app.teamId !== null && teamIds.includes(app.teamId)));
  const showSuccessorAction = app.lifecycle === "deprecated" && canManageSuccessor;

  return (
    <>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-baseline gap-3">
            <h2 className="text-2xl font-semibold text-primary">{app.displayName}</h2>
            {!permissionsLoading && (canForwardLifecycle || canReverseLifecycle) && (
              <LifecycleMenu
                application={app}
                canForward={canForwardLifecycle}
                canReverse={canReverseLifecycle}
                canOverride={canOverrideSunset}
              />
            )}
            <AssignTeamPicker applicationId={app.id} currentTeamId={app.teamId} />
          </div>
          {canEdit && (
            <Button color="secondary" size="sm" onClick={() => setEditOpen(true)}>
              Edit
            </Button>
          )}
        </div>
        <Card>
          <DetailTabs aria-label={app.displayName}>
            <DetailTabs.Tab id="overview" label="Overview">
              <div className="space-y-6">
                <section>
                  <h3 className="text-sm font-medium text-tertiary">Description</h3>
                  <p className="mt-1 text-sm text-secondary">
                    {app.description ? app.description : <span className="italic">No description</span>}
                  </p>
                </section>
                <hr className="border-secondary" />
                <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <Field label="ID" value={app.id} mono />
                  <div>
                    <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                    <div className="mt-1 text-sm"><CreatedByLink user={app.createdBy} /></div>
                  </div>
                  <Field label="Created" value={app.createdAt ?? "—"} />
                </section>
                {(app.successorApplicationId || showSuccessorAction) && (
                  <>
                    <hr className="border-secondary" />
                    <section className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="text-xs uppercase tracking-wide text-tertiary">Successor</div>
                        <div className="mt-1 text-sm">
                          {app.successorApplicationId ? (
                            <Link to={`/catalog/applications/${app.successorApplicationId}`} className="text-brand-secondary hover:underline">
                              {app.successorDisplayName ?? "—"} →
                            </Link>
                          ) : (
                            <span className="italic text-tertiary">None set</span>
                          )}
                        </div>
                      </div>
                      {showSuccessorAction && (
                        <Button color="secondary" size="sm" onClick={() => setSuccessorDialogOpen(true)}>
                          {app.successorApplicationId ? "Change successor" : "Set successor"}
                        </Button>
                      )}
                    </section>
                  </>
                )}
              </div>
            </DetailTabs.Tab>

            <DetailTabs.Tab id="dependencies" label="Dependencies">
              <div className="space-y-6">
                <Suspense fallback={<Skeleton className="h-80 w-full" />}>
                  <DependencyMiniGraph entityKind="application" entityId={app.id} displayName={app.displayName} />
                </Suspense>
                <hr className="border-secondary" />
                <ApiSurfaceSection entityKind="application" entityId={app.id} entityTeamId={app.teamId} entityDisplayName={app.displayName} />
                <hr className="border-secondary" />
                <RelationshipsSection
                  entityKind="application"
                  entityId={app.id}
                  entityTeamId={app.teamId}
                  entityDisplayName={app.displayName}
                />
              </div>
            </DetailTabs.Tab>
          </DetailTabs>
        </Card>
      </div>

      {editOpen && (
        <EditApplicationDialog application={app} open onOpenChange={setEditOpen} />
      )}

      {successorDialogOpen && (
        <SetSuccessorDialog application={app} open onOpenChange={setSuccessorDialogOpen} />
      )}
    </>
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
