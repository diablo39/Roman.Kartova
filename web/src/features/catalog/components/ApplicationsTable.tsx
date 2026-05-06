import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import {
  SortableHead, TablePager, TableSkeleton,
  fromSort, toSort,
} from "@/components/application/data-table/data-table";
import type { CursorListResult, SortDirection } from "@/lib/list/types";

export interface ApplicationRow {
  id: string;
  name: string;
  displayName: string;
  description: string;
  ownerUserId?: string;
  createdAt?: string;
}

type SortField = "createdAt" | "name";

interface Props {
  list: CursorListResult<ApplicationRow>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
}

export function ApplicationsTable({ list, sortBy, sortOrder, onSortChange }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Applications">
        <Table.Header>
          <Table.Head id="name" isRowHeader>Name</Table.Head>
          <Table.Head id="description">Description</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={3} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No applications yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Application&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if (field === "createdAt" || field === "name") {
      onSortChange(field, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table
        aria-label="Applications"
        sortDescriptor={fromSort(sortBy, sortOrder)}
        onSortChange={handleSortChange}
      >
        <Table.Header>
          <SortableHead id="name" isRowHeader>Name</SortableHead>
          <Table.Head id="description">Description</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map(app => (
            <Table.Row key={app.id} id={app.id}>
              <Table.Cell>
                <Link
                  to={`/catalog/applications/${app.id}`}
                  className="block font-medium text-primary hover:underline"
                >
                  {app.displayName}
                </Link>
                <span className="font-mono text-xs text-tertiary">{app.name}</span>
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {app.description || <span className="italic">No description</span>}
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {app.createdAt ? new Date(app.createdAt).toLocaleDateString() : ""}
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
