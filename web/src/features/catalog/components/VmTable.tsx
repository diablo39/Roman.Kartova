import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { PowerStateBadge } from "./PowerStateBadge";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { VmListItemResponse } from "@/features/catalog/api/infrastructure";
import { isPowerState } from "@/features/catalog/powerState";

// Wire names per VmSortField (Kartova.Catalog.Contracts) — camelCase, ADR-0095.
// `provider`/`powerState`/`os`/`vcpu`/`memoryGb`/`hostname`/`region` sort the JSONB
// attributes + top-level provider column (slice 2a Task 4/7); `createdAt`/
// `displayName` predate them (slice 1).
const SORT_FIELDS = [
  "createdAt",
  "displayName",
  "provider",
  "powerState",
  "os",
  "vcpu",
  "memoryGb",
  "hostname",
  "region",
] as const;
type SortField = (typeof SORT_FIELDS)[number];

function isSortField(value: string): value is SortField {
  return (SORT_FIELDS as readonly string[]).includes(value);
}

interface Props {
  list: CursorListResult<VmListItemResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
}

export function VmTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Virtual machines">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="provider">Provider</Table.Head>
          <Table.Head id="powerState">Power state</Table.Head>
          <Table.Head id="os">OS</Table.Head>
          <Table.Head id="vcpu">vCPU</Table.Head>
          <Table.Head id="memoryGb">Memory</Table.Head>
          <Table.Head id="hostname">Hostname</Table.Head>
          <Table.Head id="ipAddresses">IP addresses</Table.Head>
          <Table.Head id="region">Region</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={11} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No virtual machines yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register VM&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if (isSortField(field)) {
      onSortChange(field, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="Virtual machines" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <SortableHead id="provider">Provider</SortableHead>
          <SortableHead id="powerState">Power state</SortableHead>
          <SortableHead id="os">OS</SortableHead>
          <SortableHead id="vcpu">vCPU</SortableHead>
          <SortableHead id="memoryGb">Memory</SortableHead>
          <SortableHead id="hostname">Hostname</SortableHead>
          <Table.Head id="ipAddresses">IP addresses</Table.Head>
          <SortableHead id="region">Region</SortableHead>
          <Table.Head id="team">Team</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((vm) => {
            const ips = vm.attributes.ipAddresses ?? [];
            return (
              <Table.Row key={vm.id} id={vm.id}>
                <Table.Cell>
                  <Link
                    to={`/catalog/infrastructure/vms/${vm.id}`}
                    className="block font-medium text-primary hover:underline"
                  >
                    {vm.displayName}
                  </Link>
                </Table.Cell>
                <Table.Cell className="text-sm">{vm.provider ?? "—"}</Table.Cell>
                <Table.Cell>
                  {isPowerState(vm.attributes.powerState) ? (
                    <PowerStateBadge powerState={vm.attributes.powerState} />
                  ) : (
                    <span className="text-sm text-tertiary">{vm.attributes.powerState}</span>
                  )}
                </Table.Cell>
                <Table.Cell className="text-sm">{vm.attributes.os}</Table.Cell>
                <Table.Cell className="text-sm text-tertiary">{vm.attributes.vcpu}</Table.Cell>
                <Table.Cell className="text-sm text-tertiary">{vm.attributes.memoryGb}</Table.Cell>
                <Table.Cell className="text-sm">{vm.attributes.hostname}</Table.Cell>
                <Table.Cell className="text-sm text-tertiary">
                  {ips.length === 0 ? (
                    "—"
                  ) : (
                    <div className="flex flex-col">
                      {ips.map((ip) => (
                        <span key={ip}>{ip}</span>
                      ))}
                    </div>
                  )}
                </Table.Cell>
                <Table.Cell className="text-sm">{vm.attributes.region}</Table.Cell>
                <Table.Cell className="text-sm">
                  {teamNameById.get(vm.teamId) ?? "Unknown team"}
                </Table.Cell>
                <Table.Cell className="text-sm text-tertiary">
                  {vm.createdAt ? new Date(vm.createdAt).toLocaleDateString() : ""}
                </Table.Cell>
              </Table.Row>
            );
          })}
        </Table.Body>
      </Table>
      <TablePager
        hasPrev={list.hasPrev}
        hasNext={list.hasNext}
        onPrev={list.goPrev}
        onNext={list.goNext}
        pageSize={list.items.length}
      />
    </div>
  );
}
