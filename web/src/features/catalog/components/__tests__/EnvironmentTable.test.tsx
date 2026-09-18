import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

import { EnvironmentTable } from "../EnvironmentTable";
import type { CursorListResult } from "@/lib/list/types";
import type { EnvironmentListItemResponse } from "@/features/catalog/api/environments";

function envItem(overrides: Partial<EnvironmentListItemResponse> = {}): EnvironmentListItemResponse {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "Production",
    description: "Prod environment",
    type: "production",
    region: "eu-west-1",
    cluster: "prod-eu-west-1",
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    ...overrides,
  };
}

function stubList(items: EnvironmentListItemResponse[]): CursorListResult<EnvironmentListItemResponse> {
  return {
    items,
    isLoading: false,
    isFetching: false,
    isError: false,
    error: null,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    reset: vi.fn(),
    refetch: vi.fn(),
  };
}

function renderTable(items: EnvironmentListItemResponse[], onSortChange = vi.fn()) {
  render(
    <MemoryRouter>
      <EnvironmentTable
        list={stubList(items)}
        sortBy="displayName"
        sortOrder="asc"
        onSortChange={onSortChange}
      />
    </MemoryRouter>,
  );
  return { onSortChange };
}

describe("EnvironmentTable", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the expected columns", () => {
    renderTable([envItem()]);

    for (const name of ["Name", "Type", "Region", "Cluster", "Created"]) {
      expect(screen.getByRole("columnheader", { name })).toBeInTheDocument();
    }
  });

  it("renders each row's displayName, type badge, region, and cluster", () => {
    renderTable([
      envItem({ id: "env-1", displayName: "Production", type: "production", region: "eu-west-1", cluster: "prod-eu-west-1" }),
      envItem({ id: "env-2", displayName: "Staging", type: "staging", region: null, cluster: null }),
    ]);

    const prodRow = screen.getByRole("row", { name: /production/i });
    expect(prodRow).toHaveTextContent("Production");
    expect(prodRow).toHaveTextContent("eu-west-1");
    expect(prodRow).toHaveTextContent("prod-eu-west-1");

    const stagingRow = screen.getByRole("row", { name: /staging/i });
    // null region/cluster render as an em dash placeholder.
    expect(stagingRow).toHaveTextContent("—");
  });

  it("isRowHeader invariant: exactly the Name column is a rowheader, one per data row", () => {
    renderTable([envItem({ id: "env-1", displayName: "Production" }), envItem({ id: "env-2", displayName: "Staging" })]);

    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders.length).toBeGreaterThan(0);
    expect(rowHeaders).toHaveLength(2); // one per data row — the single isRowHeader column (displayName)
  });

  it("clicking the Region sort header reports the EnvironmentSortField wire name", async () => {
    const { onSortChange } = renderTable([envItem()]);

    await userEvent.click(screen.getByRole("columnheader", { name: /region/i }));

    expect(onSortChange).toHaveBeenCalledWith("region", "asc");
  });

  it("shows the empty state when there are no items", () => {
    renderTable([]);

    expect(screen.getByText("No environments yet")).toBeInTheDocument();
  });
});
