import React from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import * as applicationsModule from "@/features/catalog/api/applications";
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

// Default: fully permissive — existing tests are unaffected.
// Individual tests can call mockPermissions() to scope down.
const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

// Mock useTeamsList so multi-select team options render without a live API call.
const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: (..._args: unknown[]) => useTeamsListMock(),
}));

import { KartovaPermissions } from "@/shared/auth/permissions";

function mockPermissions(perms: string[]) {
  usePermissionsMock.mockReturnValue({
    role: "test",
    hasPermission: (p: string) => perms.includes(p),
    isLoading: false,
  });
}

function emptyTeams() {
  return { items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn() };
}

function oneTeam() {
  return {
    items: [{ id: "00000000-0000-0000-0000-000000000099", displayName: "Platform" }],
    isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
  };
}

function harness(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

/** Returns a cursor page envelope matching CursorPageOfApplicationResponse. */
function pageOf<T>(items: T[]) {
  return { items, nextCursor: null, prevCursor: null };
}

describe("CatalogListPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    // Fully permissive default so existing tests are unaffected.
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(emptyTeams());
  });

  it("renders heading and Register Application button", () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    expect(screen.getByRole("heading", { name: /applications/i, level: 2 })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /register application/i })).toBeInTheDocument();
  });

  it("renders empty state when API returns no rows", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText(/no applications yet/i)).toBeInTheDocument());
  });

  it("renders rows when API returns applications", async () => {
    const get = vi.fn().mockResolvedValue({
      data: pageOf([
        {
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
        },
      ]),
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText("App One")).toBeInTheDocument());
  });

  it("renders an inline error card when the list query errors", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "boom" },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(screen.getByText(/failed to load applications/i)).toBeInTheDocument());
  });

  it("toggles dialog open state when Register Application is clicked", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    const btn = screen.getByRole("button", { name: /register application/i });
    await userEvent.click(btn);
    // The dialog itself wires up in Task 18; for now the button must at least be clickable
    // and not throw.
    expect(btn).toBeInTheDocument();
  });

  it("opens the Register Application dialog on button click", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get,
      POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    await userEvent.click(screen.getByRole("button", { name: /register application/i }));
    expect(await screen.findByRole("dialog", { name: /register application/i })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// API hook params — assert that useApplicationsList receives the right query
// params as derived from URL state.
// ---------------------------------------------------------------------------

const stubListResult = {
  items: [],
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

function harnessWithApp(initialEntries: string[] = ["/"]) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={initialEntries}>
        <Routes>
          <Route path="/" element={<CatalogListPage />} />
        </Routes>
        {children}
      </MemoryRouter>
    </QueryClientProvider>
  );
}

// ---------------------------------------------------------------------------
// URL round-trip helpers (Routes + Route so useSearchParams updates the URL).
// ---------------------------------------------------------------------------

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

describe("CatalogListPage — API hook receives correct query params", () => {
  let useApplicationsListSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(emptyTeams());
    useApplicationsListSpy = vi
      .spyOn(applicationsModule, "useApplicationsList")
      .mockReturnValue(stubListResult);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the Filters search box", () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    render(<CatalogListPage />, { wrapper: harness(new QueryClient({ defaultOptions: { queries: { retry: false } } })) });
    expect(screen.getByRole("textbox", { name: /search applications/i })).toBeInTheDocument();
  });

  it("defaults sort to displayName asc", () => {
    useApplicationsListSpy = vi.spyOn(applicationsModule, "useApplicationsList").mockReturnValue(stubListResult);
    render(<></>, { wrapper: harnessWithApp(["/"]) });
    expect(useApplicationsListSpy).toHaveBeenCalledWith(
      expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }),
    );
  });

  it("passes displayNameContains=foo to useApplicationsList when URL has the param", () => {
    render(<></>, { wrapper: harnessWithApp(["/?displayNameContains=foo"]) });
    expect(useApplicationsListSpy).toHaveBeenCalledWith(
      expect.objectContaining({ displayNameContains: "foo" }),
    );
  });

});

// ---------------------------------------------------------------------------
// Lifecycle multi-select — threading tests (ADR-0107)
// ---------------------------------------------------------------------------

describe("CatalogListPage — lifecycle multi-select threading", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(oneTeam());
  });

  const lifecycleTrigger = () => screen.getByRole("button", { name: /^lifecycle/i });

  it("threads selected lifecycle to apiClient.GET as repeated params", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });
    await waitFor(() => expect(get).toHaveBeenCalled());
    get.mockClear();

    await userEvent.click(lifecycleTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "Deprecated" }));
    // Dismiss the popover so the committed value persists via hidden inputs.
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications",
        expect.objectContaining({
          params: expect.objectContaining({ query: expect.objectContaining({ lifecycle: ["deprecated"] }) }),
        }),
      ),
    );
  });

  it("threads selected teamId to apiClient.GET as repeated params", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });
    await waitFor(() => expect(get).toHaveBeenCalled());
    get.mockClear();

    // The team multi-select (separate conditional spread from lifecycle) — options from useTeamsList (oneTeam).
    await userEvent.click(screen.getByRole("button", { name: /^team/i }));
    await userEvent.click(await screen.findByRole("option", { name: "Platform" }));
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(get).toHaveBeenCalledWith(
        "/api/v1/catalog/applications",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({ teamId: ["00000000-0000-0000-0000-000000000099"] }),
          }),
        }),
      ),
    );
  });

  it("omits lifecycle/teamId from the query when nothing is selected", async () => {
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });
    await waitFor(() => expect(get).toHaveBeenCalled());
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const firstCall = get.mock.calls[0] as any[];
    const query = firstCall[1].params.query;
    expect(query.lifecycle).toBeUndefined();
    expect(query.teamId).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Filtered empty state (Slice 7)
// ---------------------------------------------------------------------------

describe("CatalogListPage — filtered empty state", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(emptyTeams());
  });

  it("shows filter-miss empty state and not the generic empty state when displayNameContains yields no rows", async () => {
    vi.spyOn(applicationsModule, "useApplicationsList").mockReturnValue({
      ...stubListResult,
      items: [],
    });

    render(<></>, { wrapper: harnessWithApp(["/?displayNameContains=zzz"]) });

    expect(await screen.findByText(/no applications match your filters/i)).toBeInTheDocument();
    expect(screen.queryByText(/no applications yet/i)).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// setFilters clobber regression — text filter must survive alongside multi-select
// ---------------------------------------------------------------------------

describe("CatalogListPage — setFilters clobber regression", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    mockPermissions(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(emptyTeams());
  });

  it("typing a name + Search keeps displayNameContains in the URL", async () => {
    const user = userEvent.setup();
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<></>, { wrapper: harnessWithRoutes(qc) });

    const input = screen.getByRole("textbox", { name: /search applications/i });
    await user.type(input, "payment");
    await user.click(screen.getByRole("button", { name: /^search$/i }));

    expect(screen.getByTestId("probe").textContent).toContain("displayNameContains=payment");
  });
});

// ---------------------------------------------------------------------------
// Register button permission gating (Slice 7)
// ---------------------------------------------------------------------------

describe("CatalogListPage — Register button gating", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useTeamsListMock.mockReturnValue(emptyTeams());
  });

  it("hides Register button for Viewer (only CatalogRead)", () => {
    mockPermissions([KartovaPermissions.CatalogRead]);

    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    expect(screen.queryByRole("button", { name: /register application/i })).toBeNull();
  });

  it("shows Register button for Member (has CatalogApplicationsRegister)", async () => {
    mockPermissions([
      KartovaPermissions.CatalogRead,
      KartovaPermissions.CatalogApplicationsRegister,
    ]);

    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<CatalogListPage />, { wrapper: harness(qc) });

    expect(screen.getByRole("button", { name: /register application/i })).toBeInTheDocument();
  });
});
