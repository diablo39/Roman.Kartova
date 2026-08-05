import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { SystemCell } from "../SystemCell";

const renderCell = (props: React.ComponentProps<typeof SystemCell>) =>
  render(<MemoryRouter><SystemCell {...props} /></MemoryRouter>);

describe("SystemCell", () => {
  it("links to the System when assigned", () => {
    renderCell({ systemId: "11111111-1111-1111-1111-111111111111", systemDisplayName: "Billing" });
    const link = screen.getByRole("link", { name: "Billing" });
    expect(link).toHaveAttribute("href", "/catalog/systems/11111111-1111-1111-1111-111111111111");
  });

  it("renders an em dash when unassigned", () => {
    renderCell({ systemId: null, systemDisplayName: null });
    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("renders an em dash when the id is present but the name is missing", () => {
    // Unreachable from today's producers — SystemRef always carries both fields, populated from
    // one query row — so this is belt-and-braces for a future caller (e.g. a write-path dialog
    // that has the id but not the name) rather than a case the list path can produce.
    renderCell({ systemId: "11111111-1111-1111-1111-111111111111", systemDisplayName: null });
    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.getByText("—")).toBeInTheDocument();   // not just "no link" — a component
                                                          // returning null would pass that alone
  });
});
