import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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
function renderPage() {
  return render(<MemoryRouter initialEntries={["/catalog/services"]}><ServicesListPage /></MemoryRouter>);
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
});
