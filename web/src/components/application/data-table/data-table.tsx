import type { ReactNode } from "react";
import { ArrowDown, ArrowUp, ChevronSelectorVertical } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Table } from "@/components/application/table/table";
import { cx } from "@/lib/utils/cx";
import type { SortDirection } from "@/lib/list/types";

interface SortableHeadProps {
  id: string;
  activeField: string | null;
  activeOrder: SortDirection;
  onSortChange: (field: string, order: SortDirection) => void;
  children: ReactNode;
}

/**
 * Sortable column header cell (<th scope="col">) with controlled sort state.
 *
 * Toggle rule:
 *  - inactive column → asc
 *  - active asc      → desc
 *  - active desc     → asc
 *
 * Renders a plain <th> (not a react-aria AriaColumn) so that aria-sort can be
 * set directly without being stripped by react-aria's filterDOMProps.  When
 * composing a full table use this inside a plain <thead><tr> or alongside
 * react-aria's Table via a custom header row.
 */
export function SortableHead({ id, activeField, activeOrder, onSortChange, children }: SortableHeadProps) {
  const isActive = activeField === id;
  const ariaSort: "ascending" | "descending" | "none" = !isActive
    ? "none"
    : activeOrder === "asc" ? "ascending" : "descending";
  const Icon = !isActive ? ChevronSelectorVertical : (activeOrder === "asc" ? ArrowUp : ArrowDown);

  const handleClick = () => {
    if (!isActive) onSortChange(id, "asc");
    else onSortChange(id, activeOrder === "asc" ? "desc" : "asc");
  };

  return (
    <th scope="col" aria-sort={ariaSort} onClick={handleClick} className="relative p-0 px-6 py-2">
      <span
        className={cx(
          "flex items-center gap-1 text-left text-xs font-semibold text-tertiary hover:text-primary cursor-pointer",
          isActive && "text-primary",
        )}
      >
        <span>{children}</span>
        <Icon className="size-3.5" aria-hidden="true" />
      </span>
    </th>
  );
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
