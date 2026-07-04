import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { ApisTable } from "../ApisTable";
import type { ApiResponse } from "@/features/catalog/api/apis";

const item: ApiResponse = {
  id: "a1", tenantId: "t", displayName: "Orders API", description: "d",
  style: "graphQL", version: "v2", specUrl: null, teamId: "team1",
  createdByUserId: "u1", createdAt: "2026-07-04T10:00:00Z", createdBy: null,
} as ApiResponse;

const baseList = {
  items: [item], isLoading: false, isError: false, error: null,
  hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(),
};

function renderTable() {
  return render(
    <MemoryRouter>
      <ApisTable list={baseList as never} sortBy="displayName" sortOrder="asc"
        onSortChange={vi.fn()} teamNameById={new Map([["team1", "Platform"]])} />
    </MemoryRouter>,
  );
}

describe("ApisTable", () => {
  it("renders at least one rowheader (ADR-0084 blank-page guard)", () => {
    renderTable();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });
  it("shows the human style label and the team name", () => {
    renderTable();
    expect(screen.getByText("GraphQL")).toBeInTheDocument();
    expect(screen.getByText("Platform")).toBeInTheDocument();
  });
});
