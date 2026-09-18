import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Badge } from "@/components/base/badges/badges";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { EnvironmentListItemResponse } from "@/features/catalog/api/environments";

// Wire names per EnvironmentSortField (Kartova.Catalog.Contracts) — camelCase, ADR-0095.
// `cluster` is NOT in the backend allowlist (no sortable column for it), so its
// Table.Head below stays a plain (non-sortable) header — mirrors VmTable's
// ipAddresses/team columns.
const SORT_FIELDS = ["displayName", "createdAt", "type", "region"] as const;
type SortField = (typeof SORT_FIELDS)[number];

function isSortField(value: string): value is SortField {
  return (SORT_FIELDS as readonly string[]).includes(value);
}

const TYPE_BADGE_COLOR: Record<string, "gray" | "warning" | "success"> = {
  development: "gray",
  staging: "warning",
  production: "success",
};

export function environmentTypeLabel(type: string): string {
  return type.length === 0 ? type : type.charAt(0).toUpperCase() + type.slice(1);
}

export function EnvironmentTypeBadge({ type, size = "sm" }: { type: string; size?: "sm" | "md" }) {
  return (
    <Badge color={TYPE_BADGE_COLOR[type] ?? "gray"} type="pill-color" size={size}>
      {environmentTypeLabel(type)}
    </Badge>
  );
}

interface Props {
  list: CursorListResult<EnvironmentListItemResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
}

export function EnvironmentTable({ list, sortBy, sortOrder, onSortChange }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Environments">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="type">Type</Table.Head>
          <Table.Head id="region">Region</Table.Head>
          <Table.Head id="cluster">Cluster</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={5} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No environments yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Environment&quot; button in the header to add your first one.
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
      <Table aria-label="Environments" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <SortableHead id="type">Type</SortableHead>
          <SortableHead id="region">Region</SortableHead>
          <Table.Head id="cluster">Cluster</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((env) => (
            <Table.Row key={env.id} id={env.id}>
              <Table.Cell>
                <Link
                  to={`/catalog/environments/${env.id}`}
                  className="block font-medium text-primary hover:underline"
                >
                  {env.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell>
                <EnvironmentTypeBadge type={env.type} />
              </Table.Cell>
              <Table.Cell className="text-sm">{env.region ?? "—"}</Table.Cell>
              <Table.Cell className="text-sm">{env.cluster ?? "—"}</Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {env.createdAt ? new Date(env.createdAt).toLocaleDateString() : ""}
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
