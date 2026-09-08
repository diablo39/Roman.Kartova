import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useVm } from "@/features/catalog/api/infrastructure";
import { useTeamsList } from "@/features/teams/api/teams";
import { PowerStateBadge } from "@/features/catalog/components/PowerStateBadge";
import { InfraTypeBadge } from "@/features/catalog/components/InfraTypeBadge";
import { isPowerState } from "@/features/catalog/powerState";

export function VmDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useVm(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

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
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-2xl font-semibold text-primary">{vm.displayName}</h2>
        <InfraTypeBadge type="virtualMachine" size="md" />
        {isPowerState(attrs.powerState) ? (
          <PowerStateBadge powerState={attrs.powerState} size="md" />
        ) : (
          <span className="text-sm text-tertiary">{attrs.powerState}</span>
        )}
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
