import { useState } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import { VmTable } from "../VmTable";
import { DeleteVmConfirm } from "../DeleteVmConfirm";
import type { CursorListResult } from "@/lib/list/types";
import type { VmDetailResponse, VmListItemResponse } from "@/features/catalog/api/infrastructure";

const TEAM_ID = "00000000-0000-0000-0000-000000000010";

function vmItem(overrides: Partial<VmListItemResponse> = {}): VmListItemResponse {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "web-01",
    description: "Web server",
    provider: "AWS",
    teamId: TEAM_ID,
    systemId: null,
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    attributes: {
      powerState: "running",
      os: "Ubuntu 24.04",
      vcpu: 2,
      memoryGb: 4,
      hostname: "web-01.internal",
      ipAddresses: ["10.0.0.1"],
      region: "eu-west-1",
    },
    ...overrides,
  };
}

function stubList(items: VmListItemResponse[]): CursorListResult<VmListItemResponse> {
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

function renderTable(items: VmListItemResponse[], onSortChange = vi.fn()) {
  render(
    <MemoryRouter>
      <VmTable
        list={stubList(items)}
        sortBy="displayName"
        sortOrder="asc"
        onSortChange={onSortChange}
        teamNameById={new Map([[TEAM_ID, "Platform"]])}
      />
    </MemoryRouter>,
  );
  return { onSortChange };
}

describe("VmTable", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the provider column value per row", () => {
    renderTable([vmItem({ displayName: "web-01", provider: "AWS" }), vmItem({ id: "vm-2", displayName: "db-01", provider: null })]);

    const webRow = screen.getByRole("row", { name: /web-01/i });
    expect(webRow).toHaveTextContent("AWS");
    const dbRow = screen.getByRole("row", { name: /db-01/i });
    expect(dbRow).toHaveTextContent("—");
  });

  it("isRowHeader invariant: exactly the Name column is a rowheader, one per data row", () => {
    renderTable([vmItem({ id: "vm-1", displayName: "web-01" }), vmItem({ id: "vm-2", displayName: "db-01" })]);

    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders.length).toBeGreaterThan(0);
    expect(rowHeaders).toHaveLength(2); // one per data row — the single isRowHeader column (displayName)
  });

  it("clicking the Provider sort header reports the VmSortField wire name", async () => {
    const { onSortChange } = renderTable([vmItem()]);

    await userEvent.click(screen.getByRole("columnheader", { name: /provider/i }));

    expect(onSortChange).toHaveBeenCalledWith("provider", "asc");
  });

  it("clicking a JSONB-attribute sort header (Power state) reports its wire name", async () => {
    const { onSortChange } = renderTable([vmItem()]);

    await userEvent.click(screen.getByRole("columnheader", { name: /power state/i }));

    expect(onSortChange).toHaveBeenCalledWith("powerState", "asc");
  });

  it("an overlay opened alongside the table does not blank-page it (isRowHeader survives)", async () => {
    const del = vi.fn().mockResolvedValue({ data: undefined, error: undefined, response: { status: 204 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn(), POST: vi.fn(), PUT: vi.fn(), DELETE: del,
    } as never);

    const vm: VmDetailResponse = {
      ...vmItem(),
      version: "v1",
    };
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    function Harness() {
      const [confirmOpen, setConfirmOpen] = useState(false);
      return (
        <MemoryRouter>
          <QueryClientProvider client={qc}>
            <VmTable
              list={stubList([vmItem()])}
              sortBy="displayName"
              sortOrder="asc"
              onSortChange={vi.fn()}
              teamNameById={new Map()}
            />
            <button onClick={() => setConfirmOpen(true)}>Open confirm</button>
            <DeleteVmConfirm vm={vm} open={confirmOpen} onOpenChange={setConfirmOpen} onDeleted={vi.fn()} />
          </QueryClientProvider>
        </MemoryRouter>
      );
    }

    render(<Harness />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole("button", { name: /open confirm/i }));

    expect(screen.getByRole("dialog", { name: /delete virtual machine/i })).toBeInTheDocument();
    // The regression this guards against: a Table missing exactly one isRowHeader
    // column throws in TableCollection.updateColumns on the heavier re-render an
    // overlay mount triggers, blank-paging the whole screen (CLAUDE.md react-aria
    // Table gotcha). Once the modal is open, react-aria correctly marks the rest of
    // the page `aria-hidden` (inert background) — that's expected a11y behavior, not
    // the crash — so `{ hidden: true }` bypasses that accessibility-tree filter and
    // asserts the table markup itself is still intact underneath (i.e. it survived
    // the re-render instead of throwing and unmounting).
    expect(screen.getAllByRole("rowheader", { hidden: true }).length).toBeGreaterThan(0);
  });
});
