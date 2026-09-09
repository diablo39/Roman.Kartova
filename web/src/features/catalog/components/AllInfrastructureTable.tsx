import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { InfraTypeBadge } from "./InfraTypeBadge";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { InfrastructureListItemResponse } from "@/features/catalog/api/infrastructure";
import { isInfraType } from "@/features/catalog/infraType";

// Wire names per VmSortField (Kartova.Catalog.Contracts) — camelCase, ADR-0095.
// `InfrastructureListItemResponse` is the shared-columns-only projection (ADR-0111
// amendment) — `provider` is its only non-shared sortable addition (slice 2a Task 7);
// the JSONB VM attributes aren't columns here.
type SortField = "createdAt" | "displayName" | "provider";

interface Props {
  list: CursorListResult<InfrastructureListItemResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
  /** Resolves systemId → displayName (parent fetches all systems once). */
  systemNameById: Map<string, string>;
}

// VM is the only infrastructure type today (ADR: InfrastructureType has one member) — every row
// links to the VM detail route. Extend this switch when a second type is added.
function detailPath(item: InfrastructureListItemResponse): string {
  return `/catalog/infrastructure/vms/${item.id}`;
}

export function AllInfrastructureTable({ list, sortBy, sortOrder, onSortChange, teamNameById, systemNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Infrastructure">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="type">Type</Table.Head>
          <Table.Head id="provider">Provider</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="system">System</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={6} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No infrastructure yet</p>
          <p className="text-sm text-tertiary">Register a virtual machine to see it listed here.</p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if (field === "createdAt" || field === "displayName") {
      onSortChange(field, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="Infrastructure" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="type">Type</Table.Head>
          <SortableHead id="provider">Provider</SortableHead>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="system">System</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((item) => (
            <Table.Row key={item.id} id={item.id}>
              <Table.Cell>
                <Link to={detailPath(item)} className="block font-medium text-primary hover:underline">
                  {item.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell>
                {isInfraType(item.type) ? (
                  <InfraTypeBadge type={item.type} />
                ) : (
                  <span className="text-sm text-tertiary">{item.type}</span>
                )}
              </Table.Cell>
              <Table.Cell className="text-sm">{item.provider ?? "—"}</Table.Cell>
              <Table.Cell className="text-sm">
                {teamNameById.get(item.teamId) ?? "Unknown team"}
              </Table.Cell>
              <Table.Cell className="text-sm">
                {item.systemId ? (systemNameById.get(item.systemId) ?? "Unknown system") : (
                  <span className="text-tertiary">—</span>
                )}
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {item.createdAt ? new Date(item.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
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
