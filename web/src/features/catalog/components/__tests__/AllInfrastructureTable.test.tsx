import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

import { AllInfrastructureTable } from "../AllInfrastructureTable";
import type { CursorListResult } from "@/lib/list/types";
import type { InfrastructureListItemResponse } from "@/features/catalog/api/infrastructure";

const TEAM_ID = "00000000-0000-0000-0000-000000000010";

function infraItem(overrides: Partial<InfrastructureListItemResponse> = {}): InfrastructureListItemResponse {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "web-01",
    description: "Web server",
    type: "Vm",
    provider: "AWS",
    teamId: TEAM_ID,
    systemId: null,
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    ...overrides,
  };
}

function stubList(items: InfrastructureListItemResponse[]): CursorListResult<InfrastructureListItemResponse> {
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

function renderTable(items: InfrastructureListItemResponse[], onSortChange = vi.fn()) {
  render(
    <MemoryRouter>
      <AllInfrastructureTable
        list={stubList(items)}
        sortBy="displayName"
        sortOrder="asc"
        onSortChange={onSortChange}
        teamNameById={new Map([[TEAM_ID, "Platform"]])}
        systemNameById={new Map()}
      />
    </MemoryRouter>,
  );
  return { onSortChange };
}

describe("AllInfrastructureTable", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  // gate-7 I1: clicking the (rendered-sortable) Provider header must forward to
  // onSortChange — handleSortChange previously guarded only "createdAt"/"displayName"
  // and silently no-op'd on "provider", contradicting the registry's committed
  // Infrastructure provider sort.
  it("clicking the Provider sort header reports the wire name", async () => {
    const { onSortChange } = renderTable([infraItem()]);

    await userEvent.click(screen.getByRole("columnheader", { name: /provider/i }));

    expect(onSortChange).toHaveBeenCalledWith("provider", "asc");
  });

  it("isRowHeader invariant: exactly the Name column is a rowheader, one per data row", () => {
    renderTable([infraItem({ id: "infra-1", displayName: "web-01" }), infraItem({ id: "infra-2", displayName: "db-01" })]);

    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders.length).toBeGreaterThan(0);
    expect(rowHeaders).toHaveLength(2); // one per data row — the single isRowHeader column (displayName)
  });
});
