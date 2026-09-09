import { useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Button } from "@/components/base/buttons/button";
import { useVm } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { PowerStateBadge } from "@/features/catalog/components/PowerStateBadge";
import { InfraTypeBadge } from "@/features/catalog/components/InfraTypeBadge";
import { EditVmDialog } from "@/features/catalog/components/EditVmDialog";
import { DeleteVmConfirm } from "@/features/catalog/components/DeleteVmConfirm";
import { isPowerState } from "@/features/catalog/powerState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

export function VmDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const query = useVm(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  const [editOpen, setEditOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  // No lifecycle gate here (unlike ApplicationDetailPage's `!== "decommissioned"`) —
  // VMs have no lifecycle/soft-delete state (ADR-0111 amendment); Edit is available
  // for any VM the caller has the register permission for. Delete is gated on its
  // own permission (T6), separate from register/edit.
  const canEdit = !permissionsLoading && hasPermission(KartovaPermissions.CatalogInfrastructureRegister);
  const canDelete = !permissionsLoading && hasPermission(KartovaPermissions.CatalogInfrastructureDelete);

  if (query.isLoading) {
    return (
      <Card data-testid="vm-detail-skeleton">
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
          <p className="text-base font-medium text-error-primary">Virtual machine not found</p>
          <p className="text-sm text-tertiary">
            It may have been deleted, or you may not have access in this tenant.
          </p>
        </CardContent>
      </Card>
    );
  }

  const vm = query.data;
  const attrs = vm.attributes;
  const ips = attrs.ipAddresses ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{vm.displayName}</h2>
          <InfraTypeBadge type="virtualMachine" size="md" />
          {isPowerState(attrs.powerState) ? (
            <PowerStateBadge powerState={attrs.powerState} size="md" />
          ) : (
            <span className="text-sm text-tertiary">{attrs.powerState}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {canEdit && (
            <Button color="secondary" size="sm" onClick={() => setEditOpen(true)}>
              Edit
            </Button>
          )}
          {canDelete && (
            <Button color="secondary-destructive" size="sm" onClick={() => setDeleteOpen(true)}>
              Delete
            </Button>
          )}
        </div>
      </div>
      <Card>
        <CardContent className="space-y-6 p-6">
          <section>
            <h3 className="text-sm font-medium text-tertiary">Description</h3>
            <p className="mt-1 text-sm text-secondary">
              {vm.description ? vm.description : <span className="italic">No description</span>}
            </p>
          </section>
          <hr className="border-secondary" />
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="ID" value={vm.id} mono />
            <Field label="Provider" value={vm.provider ?? "—"} />
            <div>
              <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
              <div className="mt-1 text-sm">
                <Link to={`/teams/${vm.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(vm.teamId) ?? "View team"}
                </Link>
              </div>
            </div>
            <div>
              <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
              <div className="mt-1 text-sm">
                <Link to={`/users/${vm.createdByUserId}`} className="font-medium text-primary hover:underline">
                  {vm.createdByUserId}
                </Link>
              </div>
            </div>
            <Field label="Created" value={vm.createdAt ? new Date(vm.createdAt).toLocaleString() : "—"} />
          </section>
          <hr className="border-secondary" />
          <section>
            <h3 className="text-sm font-medium text-tertiary">Attributes</h3>
            <div className="mt-2 grid grid-cols-1 gap-4 sm:grid-cols-3">
              <Field label="OS" value={attrs.os} />
              <Field label="Hostname" value={attrs.hostname} mono />
              <Field label="Region" value={attrs.region} />
              <Field label="vCPU" value={String(attrs.vcpu)} />
              <Field label="Memory (GB)" value={String(attrs.memoryGb)} />
            </div>
          </section>
          <hr className="border-secondary" />
          <section>
            <h3 className="text-sm font-medium text-tertiary">IP Addresses</h3>
            {ips.length === 0 ? (
              <p className="mt-1 text-sm text-tertiary italic">No IP addresses registered</p>
            ) : (
              <ul className="mt-2 flex flex-wrap gap-2">
                {ips.map((ip) => (
                  <li
                    key={ip}
                    className="rounded-md bg-secondary/40 px-2 py-1 font-mono text-xs text-primary ring-1 ring-secondary"
                  >
                    {ip}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </CardContent>
      </Card>

      {canEdit && <EditVmDialog vm={vm} open={editOpen} onOpenChange={setEditOpen} />}
      {canDelete && (
        <DeleteVmConfirm
          vm={vm}
          open={deleteOpen}
          onOpenChange={setDeleteOpen}
          onDeleted={() => navigate("/catalog/infrastructure/vms")}
        />
      )}
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
