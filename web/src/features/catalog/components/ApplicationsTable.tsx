import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import {
  SortableHead, TablePager, TableSkeleton,
  fromSort, toSort,
} from "@/components/application/data-table/data-table";
import { LifecycleBadge } from "./LifecycleBadge";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { Lifecycle } from "@/features/catalog/api/applications";

export interface ApplicationRow {
  id: string;
  displayName: string;
  description: string;
  ownerUserId?: string;
  createdAt?: string;
  lifecycle: Lifecycle;
  sunsetDate: string | null;
  teamId?: string | null;
}

type SortField = "createdAt" | "displayName";

interface Props {
  list: CursorListResult<ApplicationRow>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /**
   * Resolves a teamId to its displayName for the Team column. Provided by
   * the parent (CatalogListPage) so a single useTeamsList call covers every
   * row; missing entries fall back to "Unknown team" (e.g. stale cache).
   */
  teamNameById: Map<string, string>;
}

export function ApplicationsTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    // No sortDescriptor/onSortChange wired on the loading <Table>, so render
    // plain heads here — a SortableHead without sort wiring presents an
    // interactive sort affordance with no behavior and a misleading a11y
    // signal during the skeleton state.
    return (
      <Table aria-label="Applications">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="lifecycle">Lifecycle</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="description">Description</Table.Head>
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
    if (field === "createdAt" || field === "displayName") {
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
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="lifecycle">Lifecycle</Table.Head>
          <Table.Head id="team">Team</Table.Head>
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
              </Table.Cell>
              <Table.Cell>
                <LifecycleBadge lifecycle={app.lifecycle} sunsetDate={app.sunsetDate} />
              </Table.Cell>
              <Table.Cell className="text-sm">
                {app.teamId == null ? (
                  <span className="italic text-tertiary">Unassigned</span>
                ) : (
                  <Link
                    to={`/teams/${app.teamId}`}
                    className="text-primary hover:underline"
                  >
                    {teamNameById.get(app.teamId) ?? "Unknown team"}
                  </Link>
                )}
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
