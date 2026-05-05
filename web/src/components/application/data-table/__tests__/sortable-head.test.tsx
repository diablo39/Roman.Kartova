import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SortableHead } from "../data-table";

// SortableHead renders a plain <th> — wrap in a minimal table structure.
const Wrapper = ({ children }: { children: React.ReactNode }) => (
  <table>
    <thead>
      <tr>{children}</tr>
    </thead>
  </table>
);

describe("<SortableHead>", () => {
  it("renders aria-sort=none when not the active column", () => {
    render(
      <Wrapper>
        <SortableHead id="name" activeField={null} activeOrder="asc" onSortChange={() => {}}>Name</SortableHead>
      </Wrapper>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "none");
  });

  it("renders aria-sort=ascending when active and asc", () => {
    render(
      <Wrapper>
        <SortableHead id="name" activeField="name" activeOrder="asc" onSortChange={() => {}}>Name</SortableHead>
      </Wrapper>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "ascending");
  });

  it("clicking inactive column triggers asc; clicking active asc triggers desc; clicking active desc triggers asc", async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();

    const { rerender } = render(
      <Wrapper>
        <SortableHead id="name" activeField={null} activeOrder="asc" onSortChange={onSortChange}>Name</SortableHead>
      </Wrapper>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "asc");

    rerender(
      <Wrapper>
        <SortableHead id="name" activeField="name" activeOrder="asc" onSortChange={onSortChange}>Name</SortableHead>
      </Wrapper>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "desc");

    rerender(
      <Wrapper>
        <SortableHead id="name" activeField="name" activeOrder="desc" onSortChange={onSortChange}>Name</SortableHead>
      </Wrapper>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "asc");
  });
});
