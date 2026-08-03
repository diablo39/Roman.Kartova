import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";

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
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

// ServicesListPage's own convention: useServicesList is mocked wholesale (dedicated vi.mock of
// the module) — the page test asserts hook call params + renders the real ServicesTable off
// whatever `items` the mock returns, rather than mocking apiClient.GET. Mirrors the existing
// ServicesListPage.test.tsx harness (not CatalogListPage.test.tsx's apiClient-mock harness — the
// two pages use different mock strategies and this file follows Services' own).
const useServicesListMock = vi.fn();
vi.mock("@/features/catalog/api/services", () => ({
  useServicesList: (...a: unknown[]) => useServicesListMock(...a),
  useRegisterService: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

// Mock useSystemsList — the System filter's facet source (A1 — same shape as useTeamsList).
const useSystemsListMock = vi.fn();
vi.mock("@/features/catalog/api/systems", () => ({
  useSystemsList: (..._args: unknown[]) => useSystemsListMock(),
}));

import { ServicesListPage } from "../ServicesListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

const SYSTEM_ID = "00000000-0000-0000-0000-0000000000cc";
const SYSTEM_NAME = "Payments Platform";

function stubList(overrides: Record<string, unknown> = {}) {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
    ...overrides,
  };
}

function oneSystem() {
  return stubList({ items: [{ id: SYSTEM_ID, displayName: SYSTEM_NAME }] });
}

function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}

function baseService(overrides: Record<string, unknown> = {}) {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "Orders",
    description: "Order service",
    teamId: "00000000-0000-0000-0000-000000000010",
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdBy: { id: "00000000-0000-0000-0000-0000000000aa", displayName: "Alice Admin", email: "alice@example.com" },
    createdAt: "2026-04-30T00:00:00Z",
    health: "unknown",
    endpoints: [],
    version: "v1",
    systemId: null,
    systemDisplayName: null,
    ...overrides,
  };
}

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="probe">{loc.search}</div>;
}

function renderPage(initialPath = "/catalog/services") {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/catalog/services" element={<><ServicesListPage /><LocationProbe /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ServicesListPage — System column + filter (A2)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setPerms(Object.values(KartovaPermissions));
    useTeamsListMock.mockReturnValue(stubList());
    useSystemsListMock.mockReturnValue(oneSystem());
    useServicesListMock.mockReturnValue(stubList());
  });

  it("(a) renders a link to the System for an assigned row", () => {
    useServicesListMock.mockReturnValue(stubList({
      items: [baseService({ systemId: SYSTEM_ID, systemDisplayName: SYSTEM_NAME })],
    }));
    renderPage();

    const link = screen.getByRole("link", { name: SYSTEM_NAME });
    expect(link).toHaveAttribute("href", `/catalog/systems/${SYSTEM_ID}`);
  });

  it("(b) renders the literal em dash for an unassigned row", () => {
    useServicesListMock.mockReturnValue(stubList({ items: [baseService()] }));
    renderPage();

    expect(screen.getByText("Orders")).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("(c) selecting a System and submitting threads systemId to useServicesList and appends the repeated URL param (no ?f=)", async () => {
    renderPage();
    useServicesListMock.mockClear();

    await userEvent.click(screen.getByRole("button", { name: /^system/i }));
    await userEvent.click(await screen.findByRole("option", { name: SYSTEM_NAME }));
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(useServicesListMock).toHaveBeenCalledWith(
        expect.objectContaining({ systemId: [SYSTEM_ID] }),
      ),
    );

    const search = screen.getByTestId("probe").textContent ?? "";
    expect(search).toContain(`systemId=${SYSTEM_ID}`);
    expect(search).not.toContain("f=");
  });

  it("(d) the System option list comes from the mocked useSystemsList, not a hard-coded array", async () => {
    const OTHER_ID = "00000000-0000-0000-0000-0000000000dd";
    const OTHER_NAME = "Checkout Platform";
    useSystemsListMock.mockReturnValue(stubList({ items: [{ id: OTHER_ID, displayName: OTHER_NAME }] }));
    renderPage();

    await userEvent.click(screen.getByRole("button", { name: /^system/i }));
    expect(await screen.findByRole("option", { name: OTHER_NAME })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: SYSTEM_NAME })).not.toBeInTheDocument();
  });

  it("(e) getAllByRole('rowheader').length equals the number of seeded rows", () => {
    useServicesListMock.mockReturnValue(stubList({
      items: [
        baseService({ id: "00000000-0000-0000-0000-000000000001", displayName: "Orders" }),
        baseService({
          id: "00000000-0000-0000-0000-000000000002",
          displayName: "Billing",
          systemId: SYSTEM_ID,
          systemDisplayName: SYSTEM_NAME,
        }),
      ],
    }));
    renderPage();

    expect(screen.getByText("Orders")).toBeInTheDocument();
    expect(screen.getByText("Billing")).toBeInTheDocument();
    expect(screen.getAllByRole("rowheader").length).toBe(2);
  });
});
