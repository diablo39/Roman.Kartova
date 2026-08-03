import React from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { CatalogListPage } from "../CatalogListPage";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: {
      access_token: "t",
      profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" },
    },
  }),
}));

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

// Mock useTeamsList so the Team multi-select renders without a live API call.
const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: (..._args: unknown[]) => useTeamsListMock(),
}));

// Mock useSystemsList — the System filter's facet source (A1 — same shape as useTeamsList).
const useSystemsListMock = vi.fn();
vi.mock("@/features/catalog/api/systems", () => ({
  useSystemsList: (..._args: unknown[]) => useSystemsListMock(),
}));

import { KartovaPermissions } from "@/shared/auth/permissions";

const SYSTEM_ID = "00000000-0000-0000-0000-0000000000cc";
const SYSTEM_NAME = "Payments Platform";

function mockPermissions(perms: string[]) {
  usePermissionsMock.mockReturnValue({
    role: "test",
    hasPermission: (p: string) => perms.includes(p),
    isLoading: false,
  });
}

function emptyList() {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
  };
}

function oneSystem() {
  return {
    items: [{ id: SYSTEM_ID, displayName: SYSTEM_NAME }],
    isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
  };
}

/** Returns a cursor page envelope matching CursorPageOfApplicationResponse. */
function pageOf<T>(items: T[]) {
  return { items, nextCursor: null, prevCursor: null };
}

/** Discriminates the shared apiClient.GET mock by URL — Applications vs Systems facet. */
function mockGetByUrl(handlers: { applications?: unknown[]; systems?: unknown[] }) {
  const get = vi.fn(async (url: string) => {
    if (url === "/api/v1/catalog/applications") {
      return { data: pageOf(handlers.applications ?? []), error: undefined };
    }
    if (url === "/api/v1/catalog/systems") {
      return { data: pageOf(handlers.systems ?? []), error: undefined };
    }
    return { data: pageOf([]), error: undefined };
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
  } as never);
  return get;
}

function harness(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="probe">{loc.search}</div>;
}

function harnessWithRoutes(qc: QueryClient, initialEntries: string[] = ["/"]) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={initialEntries}>
        <Routes>
          <Route path="/" element={<><CatalogListPage /><LocationProbe /></>} />
        </Routes>
        {children}
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function baseApp(overrides: Record<string, unknown> = {}) {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "App One",
    description: "d",
    ownerUserId: "u",
    createdAt: "2026-01-01T00:00:00Z",
    lifecycle: "active",
    sunsetDate: null,
    teamId: null,
    version: "v1",
    systemId: null,
    systemDisplayName: null,
    ...overrides,
  };
}

describe("CatalogListPage — System column + filter (A2)", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(emptyList());
    useSystemsListMock.mockReturnValue(oneSystem());
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("(a) renders a link to the System for an assigned row", async () => {
    mockGetByUrl({
      applications: [
        baseApp({ id: "00000000-0000-0000-0000-000000000001", displayName: "App One", systemId: SYSTEM_ID, systemDisplayName: SYSTEM_NAME }),
      ],
    });
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    const link = await screen.findByRole("link", { name: SYSTEM_NAME });
    expect(link).toHaveAttribute("href", `/catalog/systems/${SYSTEM_ID}`);
  });

  it("(b) renders the literal em dash for an unassigned row", async () => {
    mockGetByUrl({
      applications: [baseApp({ id: "00000000-0000-0000-0000-000000000002", displayName: "App Two" })],
    });
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await screen.findByText("App Two");
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("(c) selecting a System and submitting calls apiClient.GET with systemId and appends the repeated URL param (no ?f=)", async () => {
    const get = mockGetByUrl({ applications: [] });
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc) });
    await waitFor(() => expect(get).toHaveBeenCalled());
    get.mockClear();

    await userEvent.click(screen.getByRole("button", { name: /^system/i }));
    await userEvent.click(await screen.findByRole("option", { name: SYSTEM_NAME }));
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications",
        expect.objectContaining({
          params: expect.objectContaining({ query: expect.objectContaining({ systemId: [SYSTEM_ID] }) }),
        }),
      ),
    );

    const search = screen.getByTestId("probe").textContent ?? "";
    expect(search).toContain(`systemId=${SYSTEM_ID}`);
    expect(search).not.toContain("f=");
  });

  it("(d) the System option list comes from the mocked useSystemsList, not a hard-coded array", async () => {
    const OTHER_ID = "00000000-0000-0000-0000-0000000000dd";
    const OTHER_NAME = "Checkout Platform";
    useSystemsListMock.mockReturnValue({
      items: [{ id: OTHER_ID, displayName: OTHER_NAME }],
      isLoading: false, isFetching: false, isError: false, error: null,
      hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
    });
    mockGetByUrl({ applications: [] });
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await userEvent.click(screen.getByRole("button", { name: /^system/i }));
    expect(await screen.findByRole("option", { name: OTHER_NAME })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: SYSTEM_NAME })).not.toBeInTheDocument();
  });

  it("(e) getAllByRole('rowheader').length equals the number of seeded rows", async () => {
    mockGetByUrl({
      applications: [
        baseApp({ id: "00000000-0000-0000-0000-000000000001", displayName: "App One" }),
        baseApp({ id: "00000000-0000-0000-0000-000000000002", displayName: "App Two", systemId: SYSTEM_ID, systemDisplayName: SYSTEM_NAME }),
      ],
    });
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await screen.findByText("App One");
    await screen.findByText("App Two");
    expect(screen.getAllByRole("rowheader").length).toBe(2);
  });
});
