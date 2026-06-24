import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

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

const useServicesListMock = vi.fn();
vi.mock("@/features/catalog/api/services", () => ({
  useServicesList: (...a: unknown[]) => useServicesListMock(...a),
  useRegisterService: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { ServicesListPage } from "../ServicesListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

function stubList() {
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
function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}
function renderPage(initialPath = "/catalog/services") {
  return render(<MemoryRouter initialEntries={[initialPath]}><ServicesListPage /></MemoryRouter>);
}

// ---------------------------------------------------------------------------
// Basic rendering tests — use the useServicesList mock directly.
// ---------------------------------------------------------------------------

describe("ServicesListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useServicesListMock.mockReturnValue(stubList());
    useTeamsListMock.mockReturnValue(stubList());
  });

  it("renders the Services heading", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByRole("heading", { name: /services/i })).toBeInTheDocument();
  });

  it("shows Register Service for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogServicesRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register service/i })).toBeInTheDocument();
  });

  it("hides Register Service for a user without the permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register service/i })).toBeNull();
  });

  it("shows the error card with a wired Reset when the list fails to load", async () => {
    setPerms([]);
    const reset = vi.fn();
    useServicesListMock.mockReturnValue({ ...stubList(), isError: true, error: new Error("net"), reset });
    renderPage();
    expect(screen.getByText(/failed to load services/i)).toBeInTheDocument();
    const resetBtn = screen.getByRole("button", { name: /reset/i });
    await userEvent.click(resetBtn);
    expect(reset).toHaveBeenCalled();
  });

  it("renders the Filters search box", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByRole("textbox", { name: /search services/i })).toBeInTheDocument();
  });

  it("defaults sort to displayName asc (sends it to useServicesList)", () => {
    setPerms([]);
    renderPage();
    expect(useServicesListMock).toHaveBeenCalledWith(
      expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }),
    );
  });

  it("shows a filtered empty-state when a search yields no rows", async () => {
    setPerms([]);
    renderPage("/catalog/services?displayNameContains=zzz");
    expect(await screen.findByText(/no services match your filters/i)).toBeInTheDocument();
    expect(screen.queryByText(/no services yet/i)).not.toBeInTheDocument();
  });

  it("passes displayNameContains=foo to useServicesList when URL has the param", () => {
    setPerms([]);
    renderPage("/catalog/services?displayNameContains=foo");
    expect(useServicesListMock).toHaveBeenCalledWith(
      expect.objectContaining({ displayNameContains: "foo" }),
    );
  });
});

// ---------------------------------------------------------------------------
// Team + Health multi-select — threading tests (ADR-0107).
// Assert that useServicesList receives teamId/health derived from URL state.
// ---------------------------------------------------------------------------

describe("ServicesListPage — team + health multi-select threading", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useServicesListMock.mockReturnValue(stubList());
    useTeamsListMock.mockReturnValue(oneTeam());
    setPerms(Object.values(KartovaPermissions));
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("threads selected teamId to useServicesList when URL has the teamId param", () => {
    renderPage("/catalog/services?teamId=00000000-0000-0000-0000-000000000099");
    expect(useServicesListMock).toHaveBeenCalledWith(
      expect.objectContaining({ teamId: ["00000000-0000-0000-0000-000000000099"] }),
    );
  });

  it("threads selected health to useServicesList when URL has the health param", () => {
    renderPage("/catalog/services?health=healthy");
    expect(useServicesListMock).toHaveBeenCalledWith(
      expect.objectContaining({ health: ["healthy"] }),
    );
  });

  it("omits teamId/health from useServicesList when nothing is selected", () => {
    renderPage("/catalog/services");
    // No teamId/health in URL ⇒ multiValues yields undefined/empty ⇒ params omitted.
    expect(useServicesListMock).toHaveBeenCalledWith(
      expect.objectContaining({ teamId: undefined, health: undefined }),
    );
  });

  it("threads teamId to useServicesList after user selects a team via the multi-select UI", async () => {
    renderPage("/catalog/services");

    // Open Team multi-select, pick "Platform", dismiss popover (react-aria focus-trap),
    // then submit via Search — mirrors the CatalogListPage threading tests.
    await userEvent.click(screen.getByRole("button", { name: /^team/i }));
    await userEvent.click(await screen.findByRole("option", { name: "Platform" }));
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(useServicesListMock).toHaveBeenCalledWith(
        expect.objectContaining({ teamId: ["00000000-0000-0000-0000-000000000099"] }),
      ),
    );
  });

  it("threads health to useServicesList after user selects a health status via the multi-select UI", async () => {
    renderPage("/catalog/services");

    await userEvent.click(screen.getByRole("button", { name: /^health/i }));
    await userEvent.click(await screen.findByRole("option", { name: "Healthy" }));
    await userEvent.click(document.body);
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(useServicesListMock).toHaveBeenCalledWith(
        expect.objectContaining({ health: ["healthy"] }),
      ),
    );
  });
});
