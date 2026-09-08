import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
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

// VirtualMachinesListPage's own convention (mirrors ServicesListPage.system.test.tsx):
// useVmList is mocked wholesale — the page test renders the real VmTable off whatever
// `items` the mock returns, so isRowHeader / badge / permission wiring are all exercised
// through the real DOM rather than through a mocked Table.
const useVmListMock = vi.fn();
vi.mock("@/features/catalog/api/infrastructure", () => ({
  useVmList: (...a: unknown[]) => useVmListMock(...a),
  useRegisterVm: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { VirtualMachinesListPage } from "../VirtualMachinesListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

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

function baseVm(overrides: Record<string, unknown> = {}) {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "web-01",
    description: "Web server",
    teamId: TEAM_ID,
    systemId: null,
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    attributes: {
      powerState: "running",
      os: "Ubuntu 24.04",
      vcpu: 2,
      memoryGb: 4,
      hostname: "web-01.internal",
      ipAddresses: ["10.0.0.1"],
      region: "eu-west-1",
    },
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/catalog/infrastructure/vms"]}>
      <VirtualMachinesListPage />
    </MemoryRouter>,
  );
}

describe("VirtualMachinesListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(stubList());
    useVmListMock.mockReturnValue(stubList());
  });

  it("renders rows from the mocked useVmList, with a power-state badge and the isRowHeader guard", () => {
    setPerms([]);
    useVmListMock.mockReturnValue(stubList({
      items: [
        baseVm({ id: "00000000-0000-0000-0000-000000000001", displayName: "web-01" }),
        baseVm({
          id: "00000000-0000-0000-0000-000000000002",
          displayName: "db-01",
          attributes: {
            powerState: "stopped",
            os: "Windows Server 2022",
            vcpu: 4,
            memoryGb: 16,
            hostname: "db-01.internal",
            ipAddresses: ["10.0.0.2"],
            region: "eu-west-1",
          },
        }),
      ],
    }));

    renderPage();

    // (1) Rows render — both seeded displayNames appear.
    expect(screen.getByText("web-01")).toBeInTheDocument();
    expect(screen.getByText("db-01")).toBeInTheDocument();

    // (2) isRowHeader guard — jsdom structurally catches a missing/zero rowheader
    // that a plain render assertion would not (react-aria Table throws in
    // TableCollection.updateColumns without exactly one isRowHeader column).
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);

    // (3) PowerStateBadge renders the seeded label text — scoped to each row (the
    // powerState filter's own <select> also contains an option with the same text).
    const webRow = screen.getByRole("row", { name: /web-01/i });
    expect(within(webRow).getByText("Running")).toBeInTheDocument();
    const dbRow = screen.getByRole("row", { name: /db-01/i });
    expect(within(dbRow).getByText("Stopped")).toBeInTheDocument();
  });

  it("hides Register VM for a user without the register permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register vm/i })).toBeNull();
  });

  it("shows Register VM for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogInfrastructureRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register vm/i })).toBeInTheDocument();
  });
});
