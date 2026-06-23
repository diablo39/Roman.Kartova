import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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

function emptyList() {
  return { items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn() };
}
function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}
function renderPage(initialPath = "/catalog/services") {
  return render(<MemoryRouter initialEntries={[initialPath]}><ServicesListPage /></MemoryRouter>);
}

describe("ServicesListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useServicesListMock.mockReturnValue(emptyList());
    useTeamsListMock.mockReturnValue(emptyList());
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
    useServicesListMock.mockReturnValue({ ...emptyList(), isError: true, error: new Error("net"), reset });
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
    expect(await screen.findByText(/no services match your search/i)).toBeInTheDocument();
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
