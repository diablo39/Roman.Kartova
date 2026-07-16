import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { serviceKeys } from "@/features/catalog/api/services";
import { ServiceDetailPage } from "../ServiceDetailPage";
import * as relationshipsApi from "@/features/catalog/api/relationships";
import * as apiSurfaceApi from "@/features/catalog/api/apiSurface";

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

// Stub API surface (Dependencies tab) so ApiSurfaceSection renders without real API calls.
vi.mock("@/features/catalog/api/apiSurface", () => ({
  useApiSurface: () => ({ data: { provides: [], consumes: [] }, isLoading: false, isError: false }),
}));

// Stub derived dependencies (Dependencies tab) so DerivedDependenciesSection + DependencyMiniGraph render without real API calls.
vi.mock("@/features/catalog/api/derivedDependencies", () => ({
  useDerivedDependencies: () => ({ data: { dependencies: [], dependents: [] }, isLoading: false, isError: false }),
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

const tabsSvc = { ...svc, id: "00000000-0000-0000-0000-000000000099", displayName: "Checkout Service" };

/**
 * Renders the page with the Dependencies-tab fixture (`tabsSvc`) already sitting in the
 * QueryClient cache — synchronous, no `waitFor` needed. `search` is appended to the route
 * (e.g. `"?tab=dependencies"`), mirroring the ApiDetailPage test harness.
 */
function renderPage(search = "") {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  qc.setQueryData(serviceKeys.detail(tabsSvc.id), tabsSvc);
  return render(harness(qc, `/catalog/services/${tabsSvc.id}${search}`));
}

describe("ServiceDetailPage", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("keeps the endpoints table on Overview with a row header", () => {
    renderPage(); // default overview
    expect(screen.getByRole("heading", { name: "Checkout Service" })).toBeInTheDocument();
    // endpoints table lives on Overview (default tab) — ADR-0084 guard
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });

  it("moves dependency + API-surface + relationships sections to the Dependencies tab", () => {
    renderPage("?tab=dependencies");
    expect(screen.getByRole("tab", { name: "Dependencies" })).toHaveAttribute("aria-selected", "true");
    // RelationshipsSection renders on the Dependencies tab (no literal "relationship" text on
    // screen — the word only appears in an aria-label, not visible text — so assert its
    // "Incoming" group heading instead; see task-3-report.md for the rationale).
    expect(screen.getByText("Incoming")).toBeInTheDocument();
  });

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

// ---------------------------------------------------------------------------
// Relationships/API-surface dedup (slice #71 — excludeApiEdges)
// ---------------------------------------------------------------------------

describe("ServiceDetailPage — Relationships/API-surface dedup (slice #71)", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("does not show a provided API as both an API-surface entry and an Outgoing relationship row", () => {
    // Mirrors real backend behaviour: outgoing relationships omit providesApiFor/consumesApiFrom
    // edges once excludeApiEdges=true is sent (variant="full" on this page).
    vi.spyOn(relationshipsApi, "useRelationshipsList").mockImplementation(
      ((p: { direction: string; excludeApiEdges?: boolean }) => ({
        items:
          p.direction === "outgoing" && !p.excludeApiEdges
            ? [
                {
                  id: "r-api",
                  type: "providesApiFor",
                  origin: "manual",
                  source: { kind: "service", id: tabsSvc.id, displayName: tabsSvc.displayName },
                  target: { kind: "api", id: "api-1", displayName: "Orders API" },
                  createdByUserId: "u1",
                  createdAt: "2026-01-01T00:00:00Z",
                },
              ]
            : [],
        isLoading: false,
        isError: false,
        hasNext: false,
        hasPrev: false,
        goNext: vi.fn(),
        goPrev: vi.fn(),
      })) as never,
    );
    vi.spyOn(apiSurfaceApi, "useApiSurface").mockReturnValue({
      data: {
        provides: [
          {
            apiId: "api-1",
            displayName: "Orders API",
            style: "rest",
            version: "v1",
            hasSpec: true,
            origin: "direct",
            viaApplicationId: null,
            viaApplicationDisplayName: null,
          },
        ],
        consumes: [],
      },
      isLoading: false,
      isError: false,
    } as never);

    renderPage("?tab=dependencies");

    expect(screen.getAllByText("Orders API")).toHaveLength(1);
  });
});
