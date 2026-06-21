import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { HealthBadge } from "./HealthBadge";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { ServiceResponse } from "@/features/catalog/api/services";

type SortField = "createdAt" | "displayName";

interface Props {
  list: CursorListResult<ServiceResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
}

export function ServicesTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Services">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="health">Health</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="endpoints">Endpoints</Table.Head>
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
          <p className="text-base font-medium text-primary">No services yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Service&quot; button in the header to add your first one.
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
      <Table aria-label="Services" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="health">Health</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="endpoints">Endpoints</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((svc) => (
            <Table.Row key={svc.id} id={svc.id}>
              <Table.Cell>
                <Link to={`/catalog/services/${svc.id}`} className="block font-medium text-primary hover:underline">
                  {svc.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell>
                <HealthBadge health={svc.health} />
              </Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${svc.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(svc.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={svc.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">{svc.endpoints.length}</Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {svc.createdAt ? new Date(svc.createdAt).toLocaleDateString() : ""}
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
