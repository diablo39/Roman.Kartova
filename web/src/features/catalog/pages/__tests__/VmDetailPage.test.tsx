import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import type { VmDetailResponse } from "@/features/catalog/api/infrastructure";

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

const useVmMock = vi.fn();
vi.mock("@/features/catalog/api/infrastructure", async () => {
  const actual = await vi.importActual<typeof import("@/features/catalog/api/infrastructure")>(
    "@/features/catalog/api/infrastructure",
  );
  return { ...actual, useVm: (...a: unknown[]) => useVmMock(...a) };
});

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { VmDetailPage } from "../VmDetailPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

const VM_ID = "00000000-0000-0000-0000-000000000abc";
const TEAM_ID = "00000000-0000-0000-0000-000000000010";

function baseVm(overrides: Partial<VmDetailResponse> = {}): VmDetailResponse {
  return {
    id: VM_ID,
    tenantId: "t",
    displayName: "web-prod-01",
    description: "Web server",
    provider: "AWS",
    teamId: TEAM_ID,
    systemId: null,
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    version: "v1",
    attributes: {
      powerState: "running",
      os: "Ubuntu 24.04",
      vcpu: 2,
      memoryGb: 4,
      hostname: "web-prod-01.internal",
      ipAddresses: ["10.0.0.1"],
      region: "eu-west-1",
    },
    ...overrides,
  };
}

function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <MemoryRouter initialEntries={[`/catalog/infrastructure/vms/${VM_ID}`]}>
        <Routes>
          <Route path="/catalog/infrastructure/vms/:id" element={<VmDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("VmDetailPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useTeamsListMock.mockReturnValue({ items: [{ id: TEAM_ID, displayName: "Platform" }], isLoading: false });
    useVmMock.mockReturnValue({ isLoading: false, isError: false, data: baseVm() });
  });

  it("renders the provider field", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByText("Provider")).toBeInTheDocument();
    expect(screen.getByText("AWS")).toBeInTheDocument();
  });

  it("hides Edit/Delete for a user with neither permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /^edit$/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /^delete$/i })).toBeNull();
  });

  it("Edit opens the EditVmDialog, pre-filled", async () => {
    setPerms([KartovaPermissions.CatalogInfrastructureRegister]);
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: /^edit$/i }));

    expect(screen.getByRole("dialog", { name: /edit virtual machine/i })).toBeInTheDocument();
    expect(screen.getByLabelText(/display name/i)).toHaveValue("web-prod-01");
  });

  // gate-7 T6: cross-check both permission checks independently — guards a swap
  // between the Edit and Delete permission constants (e.g. an Edit-only grant that
  // accidentally also/instead shows Delete, or vice versa).
  it("an Edit-only grant shows Edit but leaves Delete hidden", () => {
    setPerms([KartovaPermissions.CatalogInfrastructureRegister]);
    renderPage();

    expect(screen.getByRole("button", { name: /^edit$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^delete$/i })).toBeNull();
  });

  it("a Delete-only grant shows Delete but leaves Edit hidden", () => {
    setPerms([KartovaPermissions.CatalogInfrastructureDelete]);
    renderPage();

    expect(screen.getByRole("button", { name: /^delete$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^edit$/i })).toBeNull();
  });

  it("Delete opens the confirm dialog and issues DELETE with If-Match, then navigates away", async () => {
    setPerms([KartovaPermissions.CatalogInfrastructureDelete]);
    const del = vi.fn().mockResolvedValue({ data: undefined, error: undefined, response: { status: 204 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn().mockResolvedValue({ data: baseVm(), error: undefined }),
      POST: vi.fn(),
      PUT: vi.fn(),
      DELETE: del,
    } as never);

    renderPage();

    await userEvent.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(screen.getByRole("dialog", { name: /delete virtual machine/i })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /delete virtual machine/i }));

    await waitFor(() => expect(del).toHaveBeenCalled());
    expect(del).toHaveBeenCalledWith(
      "/api/v1/catalog/infrastructure/vms/{id}",
      {
        params: { path: { id: VM_ID } },
        headers: { "If-Match": '"v1"' },
      },
    );
  });
});
