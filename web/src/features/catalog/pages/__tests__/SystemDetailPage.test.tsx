import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { systemKeys } from "@/features/catalog/api/systems";
import { SystemDetailPage } from "../SystemDetailPage";

vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform Team" }], isLoading: false }),
}));
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: () => ({ items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() }),
}));
// SystemDiagram (Task 9) fetches through useGraph — an empty result renders its "No members
// yet." state without needing @xyflow/react mounted, keeping this page test focused on wiring.
vi.mock("@/features/catalog/api/graph", () => ({
  useGraph: () => ({ results: [], isLoading: false, isError: false }),
}));
// SystemMembersSection (Task 9) gates Assign/Remove on usePermissions — outside an
// AuthProvider, `useAuth()` returns undefined and `auth.isAuthenticated` throws, so every
// test on this page (not just the Members-tab one) needs this mocked.
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({
    hasPermission: () => true, role: "OrgAdmin", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
  }),
}));

const sys = { id: "sys1", tenantId: "t1", displayName: "Payments Platform", description: "Money movement", teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null };

function harness(qc: QueryClient, path: string) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes><Route path="/catalog/systems/:id" element={<SystemDetailPage />} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}
function renderCached(search = "") {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  qc.setQueryData(systemKeys.detail(sys.id), sys);
  return render(harness(qc, `/catalog/systems/${sys.id}${search}`));
}

describe("SystemDetailPage", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("renders Overview fields (default tab) with the description", () => {
    renderCached();
    expect(screen.getByRole("heading", { name: "Payments Platform" })).toBeInTheDocument();
    expect(screen.getByText("Money movement")).toBeInTheDocument();
    expect(screen.getByText("Platform Team")).toBeInTheDocument();
  });

  it("renders 'No description' italic placeholder when description is null", () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
    qc.setQueryData(systemKeys.detail(sys.id), { ...sys, description: null });
    render(harness(qc, `/catalog/systems/${sys.id}`));
    const placeholder = screen.getByText("No description");
    expect(placeholder).toBeInTheDocument();
    expect(placeholder.tagName.toLowerCase()).toBe("span");
    expect(placeholder).toHaveClass("italic");
  });

  it("switches to the Members tab and shows the empty state (with a row header once loaded)", async () => {
    renderCached();
    await userEvent.click(screen.getByRole("tab", { name: /Members/i }));
    expect(screen.getByRole("tab", { name: /Members/i })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText("No components assigned yet.")).toBeInTheDocument();
  });

  it("renders a skeleton while loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {}));
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(harness(qc, "/catalog/systems/sys1"));
    expect(container.querySelectorAll('[data-testid="system-detail-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders the diagram above the members table on the Members tab (FU-A)", async () => {
    renderCached();
    await userEvent.click(screen.getByRole("tab", { name: "Members" }));

    const diagram = await screen.findByRole("region", { name: /system diagram/i });
    const members = screen.getByRole("region", { name: /members/i });
    expect(diagram).toBeInTheDocument();
    // DOM order = visual order: the diagram precedes the members section.
    expect(diagram.compareDocumentPosition(members) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows a not-found card on 404", async () => {
    const get = vi.fn().mockResolvedValue({ data: undefined, error: { status: 404, title: "Not Found" } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/systems/missing"));
    await waitFor(() => expect(screen.getByText("System not found")).toBeInTheDocument());
  });
});
