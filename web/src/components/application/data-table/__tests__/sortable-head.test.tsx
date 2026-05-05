import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { SortDescriptor } from "react-aria-components";
import { Table } from "@/components/application/table/table";
import { SortableHead, fromSort, toSort } from "../data-table";

describe("<SortableHead> integrated with <Table>", () => {
  it("renders aria-sort=none when not the active column", () => {
    render(
      <Table aria-label="t" sortDescriptor={fromSort("createdAt", "asc")} onSortChange={() => {}}>
        <Table.Header>
          <SortableHead id="name">Name</SortableHead>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "none");
  });

  it("renders aria-sort=ascending when active and asc", () => {
    render(
      <Table aria-label="t" sortDescriptor={fromSort("name", "asc")} onSortChange={() => {}}>
        <Table.Header>
          <SortableHead id="name">Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "ascending");
  });

  it("clicking a column emits onSortChange via react-aria", async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();

    render(
      <Table aria-label="t" sortDescriptor={fromSort("createdAt", "desc")} onSortChange={onSortChange}>
        <Table.Header>
          <SortableHead id="name">Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );

    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenCalledOnce();
    const arg = onSortChange.mock.calls[0]![0] as SortDescriptor;
    expect(toSort(arg)).toEqual({ field: "name", order: "asc" });
  });

  it("toSort/fromSort round-trip our (field, order) shape", () => {
    const desc = fromSort("name", "desc")!;
    expect(toSort(desc)).toEqual({ field: "name", order: "desc" });
  });
});
