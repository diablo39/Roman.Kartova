import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { ServiceDetailPage } from "../ServiceDetailPage";

// useTeamsList is only used to resolve the team name link — stub it out.
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "00000000-0000-0000-0000-000000000010", displayName: "Platform" }], isLoading: false }),
}));

// Stub permissions so RelationshipsSection renders without an auth provider.
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: () => false, role: "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false }),
}));

// Stub relationships so RelationshipsSection renders without real API calls.
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: () => ({ items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() }),
  useDeleteRelationship: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

function harness(qc: QueryClient, path: string) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/catalog/services/:id" element={<ServiceDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const svc = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  displayName: "Orders",
  description: "Order service",
  teamId: "00000000-0000-0000-0000-000000000010",
  createdByUserId: "00000000-0000-0000-0000-0000000000aa",
  createdBy: { id: "00000000-0000-0000-0000-0000000000aa", displayName: "Alice Admin", email: "alice@example.com" },
  createdAt: "2026-01-01T12:34:56Z",
  health: "unknown",
  endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
  version: "v1",
};

describe("ServiceDetailPage", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("renders the service header, Unknown health, and an endpoint", async () => {
    const get = vi.fn().mockResolvedValue({ data: svc, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/${svc.id}`));

    await waitFor(() => expect(screen.getByText("Orders")).toBeInTheDocument());
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByText("https://api.example.com/v1")).toBeInTheDocument();
    expect(screen.getByText("REST")).toBeInTheDocument();
  });

  it("renders the 'No endpoints' empty state", async () => {
    const get = vi.fn().mockResolvedValue({ data: { ...svc, endpoints: [] }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/${svc.id}`));
    await waitFor(() => expect(screen.getByText(/no endpoints registered/i)).toBeInTheDocument());
  });

  it("renders a skeleton while loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {}));
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(harness(qc, `/catalog/services/${svc.id}`));
    expect(container.querySelectorAll('[data-testid="service-detail-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders a not-found card on error", async () => {
    const get = vi.fn().mockResolvedValue({ data: undefined, error: { status: 404, title: "Not Found" } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/missing`));
    await waitFor(() => expect(screen.getByText(/service not found/i)).toBeInTheDocument());
  });
});
