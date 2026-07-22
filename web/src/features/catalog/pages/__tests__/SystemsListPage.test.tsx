import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: "t", profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" } } }),
}));

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

const useSystemsListMock = vi.fn();
vi.mock("@/features/catalog/api/systems", () => ({
  useSystemsList: (...a: unknown[]) => useSystemsListMock(...a),
  useRegisterSystem: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { SystemsListPage } from "../SystemsListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

const TEAM_ID = "00000000-0000-0000-0000-000000000099";
function stubList(over: Record<string, unknown> = {}) {
  return { items: [], isLoading: false, isFetching: false, isError: false, error: null, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(), ...over };
}
function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}
function renderPage(path = "/catalog/systems") {
  return render(<MemoryRouter initialEntries={[path]}><SystemsListPage /></MemoryRouter>);
}

describe("SystemsListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useSystemsListMock.mockReturnValue(stubList());
    useTeamsListMock.mockReturnValue(stubList({ items: [{ id: TEAM_ID, displayName: "Platform" }] }));
  });

  it("renders the Systems heading", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByRole("heading", { name: /systems/i })).toBeInTheDocument();
  });

  it("shows Register System for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogSystemsRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register system/i })).toBeInTheDocument();
  });

  it("hides Register System for a user without the permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register system/i })).toBeNull();
  });

  it("defaults sort to displayName asc (sends it to useSystemsList)", () => {
    setPerms([]);
    renderPage();
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }));
  });

  it("threads displayNameContains from the URL to useSystemsList", () => {
    setPerms([]);
    renderPage("/catalog/systems?displayNameContains=pay");
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ displayNameContains: "pay" }));
  });

  it("threads teamId from the URL to useSystemsList", () => {
    setPerms([]);
    renderPage(`/catalog/systems?teamId=${TEAM_ID}`);
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ teamId: [TEAM_ID] }));
  });

  it("shows a filtered empty-state when a search yields no rows", async () => {
    setPerms([]);
    renderPage("/catalog/systems?displayNameContains=zzz");
    expect(await screen.findByText(/no systems match your filters/i)).toBeInTheDocument();
  });
});
