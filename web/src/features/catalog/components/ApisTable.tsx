import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { Badge } from "@/components/base/badges/badges";
import { API_STYLE_LABEL } from "@/features/catalog/schemas/registerApi";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { ApiResponse } from "@/features/catalog/api/apis";

type SortField = "displayName" | "style" | "version" | "createdAt";
const SORT_FIELDS: readonly SortField[] = ["displayName", "style", "version", "createdAt"];

interface Props {
  list: CursorListResult<ApiResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  teamNameById: Map<string, string>;
}

export function ApisTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="APIs">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="style">Style</Table.Head>
          <Table.Head id="version">Version</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
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
          <p className="text-base font-medium text-primary">No APIs yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register API&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if ((SORT_FIELDS as readonly string[]).includes(field)) {
      onSortChange(field as SortField, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="APIs" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <SortableHead id="style">Style</SortableHead>
          <SortableHead id="version">Version</SortableHead>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((api) => (
            <Table.Row key={api.id} id={api.id}>
              <Table.Cell>
                <Link to={`/catalog/apis/${api.id}`} className="block font-medium text-primary hover:underline">
                  {api.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <Badge type="pill-color" color="gray" size="sm">
                  {API_STYLE_LABEL[api.style]}
                </Badge>
              </Table.Cell>
              <Table.Cell className="font-mono text-sm text-tertiary">{api.version}</Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${api.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(api.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={api.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {api.createdAt ? new Date(api.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager hasPrev={list.hasPrev} hasNext={list.hasNext} onPrev={list.goPrev} onNext={list.goNext} pageSize={list.items.length} />
    </div>
  );
}
