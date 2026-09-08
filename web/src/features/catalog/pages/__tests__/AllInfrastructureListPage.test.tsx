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

const useInfrastructureListMock = vi.fn();
vi.mock("@/features/catalog/api/infrastructure", () => ({
  useInfrastructureList: (...a: unknown[]) => useInfrastructureListMock(...a),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

const useSystemsListMock = vi.fn();
vi.mock("@/features/catalog/api/systems", () => ({
  useSystemsList: (..._args: unknown[]) => useSystemsListMock(),
}));

import { AllInfrastructureListPage } from "../AllInfrastructureListPage";

const TEAM_ID = "00000000-0000-0000-0000-000000000010";

function stubList(overrides: Record<string, unknown> = {}) {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
    ...overrides,
  };
}

function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}

function baseItem(overrides: Record<string, unknown> = {}) {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "web-01",
    description: "Web server",
    type: "virtualMachine",
    teamId: TEAM_ID,
    systemId: null,
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/catalog/infrastructure"]}>
      <AllInfrastructureListPage />
    </MemoryRouter>,
  );
}

describe("AllInfrastructureListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setPerms([]);
    useTeamsListMock.mockReturnValue(stubList());
    useSystemsListMock.mockReturnValue(stubList());
    useInfrastructureListMock.mockReturnValue(stubList());
  });

  it("renders rows from the mocked useInfrastructureList and satisfies the isRowHeader guard", () => {
    useInfrastructureListMock.mockReturnValue(stubList({
      items: [
        baseItem({ id: "00000000-0000-0000-0000-000000000001", displayName: "web-01" }),
        baseItem({ id: "00000000-0000-0000-0000-000000000002", displayName: "db-01" }),
      ],
    }));

    renderPage();

    expect(screen.getByText("web-01")).toBeInTheDocument();
    expect(screen.getByText("db-01")).toBeInTheDocument();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });
});
