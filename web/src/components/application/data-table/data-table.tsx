import type { ReactNode } from "react";
import type { SortDescriptor } from "react-aria-components";
import { Button } from "@/components/base/buttons/button";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Table } from "@/components/application/table/table";
import type { SortDirection } from "@/lib/list/types";

interface SortableHeadProps {
  id: string;
  isRowHeader?: boolean;
  children: ReactNode;
}

/**
 * Sortable column header. Wraps Untitled UI's <Table.Head> with
 * allowsSorting=true. Sort state (which column, which direction) lives on
 * the parent <Table> via sortDescriptor + onSortChange — react-aria sets
 * aria-sort automatically and emits onSortChange when the header is clicked.
 *
 * Toggle rule (handled by react-aria's default Table behavior):
 *  - inactive column → ascending
 *  - active asc → descending
 *  - active desc → ascending
 *
 * Per ADR-0095 §6.2.
 */
export function SortableHead({ id, isRowHeader, children }: SortableHeadProps) {
  return (
    <Table.Head id={id} isRowHeader={isRowHeader} allowsSorting>
      {children}
    </Table.Head>
  );
}

/**
 * Convert react-aria's SortDescriptor into our (field, order) shape.
 * The Untitled UI/RAC <Table> emits `direction: "ascending"|"descending"`;
 * our wire contract uses `"asc"|"desc"` (per ADR-0095). Also tolerates the
 * undefined `column` case (no active sort) by passing through `null`.
 */
export function toSort(descriptor: SortDescriptor): { field: string; order: SortDirection } {
  return {
    field: String(descriptor.column),
    order: descriptor.direction === "ascending" ? "asc" : "desc",
  };
}

export function fromSort(field: string, order: SortDirection): SortDescriptor {
  return {
    column: field,
    direction: order === "asc" ? "ascending" : "descending",
  };
}

interface TablePagerProps {
  hasPrev: boolean;
  hasNext: boolean;
  onPrev: () => void;
  onNext: () => void;
  pageSize: number;
}

export function TablePager({ hasPrev, hasNext, onPrev, onNext, pageSize }: TablePagerProps) {
  return (
    <div className="flex items-center justify-between border-t border-secondary bg-primary px-6 py-3">
      <span className="text-sm text-tertiary">{pageSize} results</span>
      <div className="flex gap-2">
        <Button size="sm" color="secondary" onClick={onPrev} isDisabled={!hasPrev}>Prev</Button>
        <Button size="sm" color="secondary" onClick={onNext} isDisabled={!hasNext}>Next</Button>
      </div>
    </div>
  );
}

export function TableSkeleton({ rows = 5, cells = 2 }: { rows?: number; cells?: number }) {
  return (
    <Table.Body>
      {Array.from({ length: rows }).map((_, i) => (
        <Table.Row key={i} id={`skeleton-${i}`} data-testid="row-skeleton">
          {Array.from({ length: cells }).map((__, j) => (
            <Table.Cell key={j}><Skeleton className="h-5 w-40" /></Table.Cell>
          ))}
        </Table.Row>
      ))}
    </Table.Body>
  );
}
