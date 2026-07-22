import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { SystemsTable } from "../SystemsTable";
import type { CursorListResult } from "@/lib/list/types";
import type { SystemResponse } from "@/features/catalog/api/systems";

// Full CursorListResult default (matches ServicesTable.test.tsx) — no unsafe cast; includes
// the isFetching/refetch fields the type requires so future drift is type-checked.
function makeList(over: Partial<CursorListResult<SystemResponse>> = {}): CursorListResult<SystemResponse> {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasPrev: false, hasNext: false, goPrev: vi.fn(), goNext: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
    ...over,
  };
}
const sys = (over: Partial<SystemResponse> = {}): SystemResponse => ({ id: "s1", tenantId: "t1", displayName: "Alpha", description: null, teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null, ...over } as SystemResponse);

const render1 = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("SystemsTable", () => {
  const teamNames = new Map([["team1", "Platform Team"]]);

  it("renders a row with a name link and steward team", () => {
    render1(<SystemsTable list={makeList({ items: [sys()] })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getByRole("link", { name: "Alpha" })).toHaveAttribute("href", "/catalog/systems/s1");
    expect(screen.getByText("Platform Team")).toBeInTheDocument();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084
  });

  it("renders a loading skeleton (still has a row header) when loading", () => {
    render1(<SystemsTable list={makeList({ isLoading: true })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084 (loading branch)
  });

  it("shows an empty state when there are no systems", () => {
    render1(<SystemsTable list={makeList({ items: [] })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getByText("No systems yet")).toBeInTheDocument();
  });

  it("invokes onSortChange when the Name header is activated", async () => {
    const onSortChange = vi.fn();
    render1(<SystemsTable list={makeList({ items: [sys()] })} sortBy="displayName" sortOrder="asc" onSortChange={onSortChange} teamNameById={teamNames} />);
    await userEvent.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenCalledWith("displayName", "desc");
  });
});
