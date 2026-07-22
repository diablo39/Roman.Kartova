import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { SystemResponse } from "@/features/catalog/api/systems";

type SortField = "createdAt" | "displayName";

interface Props {
  list: CursorListResult<SystemResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
}

export function SystemsTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Systems">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="team">Steward team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={4} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No systems yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register System&quot; button in the header to add your first one.
          </p>
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
      <Table aria-label="Systems" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="team">Steward team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((sys) => (
            <Table.Row key={sys.id} id={sys.id}>
              <Table.Cell>
                <Link to={`/catalog/systems/${sys.id}`} className="block font-medium text-primary hover:underline">
                  {sys.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${sys.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(sys.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={sys.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {sys.createdAt ? new Date(sys.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager hasPrev={list.hasPrev} hasNext={list.hasNext} onPrev={list.goPrev} onNext={list.goNext} pageSize={list.items.length} />
    </div>
  );
}
