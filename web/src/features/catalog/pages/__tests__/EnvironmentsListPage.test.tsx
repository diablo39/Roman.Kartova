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

// EnvironmentsListPage's own convention (mirrors VirtualMachinesListPage.test.tsx):
// useEnvironmentsList is mocked wholesale — the page test renders the real EnvironmentTable
// off whatever `items` the mock returns, so isRowHeader / badge / permission wiring are all
// exercised through the real DOM rather than through a mocked Table.
const useEnvironmentsListMock = vi.fn();
vi.mock("@/features/catalog/api/environments", () => ({
  useEnvironmentsList: (...a: unknown[]) => useEnvironmentsListMock(...a),
  useRegisterEnvironment: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

import { EnvironmentsListPage } from "../EnvironmentsListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

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

// displayName is deliberately NOT "Production"/"Staging" — the Type column renders a badge
// with the Title-cased type label ("Production"/"Staging"), which would otherwise collide
// with a getByText lookup on the row's own displayName link.
function envItem(overrides: Record<string, unknown> = {}) {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    tenantId: "t",
    displayName: "Prod Environment",
    description: "Prod environment",
    type: "production",
    region: "eu-west-1",
    cluster: "prod-eu-west-1",
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/catalog/environments"]}>
      <EnvironmentsListPage />
    </MemoryRouter>,
  );
}

describe("EnvironmentsListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useEnvironmentsListMock.mockReturnValue(stubList());
  });

  it("renders rows from the mocked useEnvironmentsList, with the isRowHeader guard", () => {
    setPerms([]);
    useEnvironmentsListMock.mockReturnValue(stubList({
      items: [
        envItem({ id: "00000000-0000-0000-0000-000000000001", displayName: "Prod Environment" }),
        envItem({ id: "00000000-0000-0000-0000-000000000002", displayName: "Stage Environment", type: "staging" }),
      ],
    }));

    renderPage();

    // (1) Rows render — both seeded displayNames appear.
    expect(screen.getByText("Prod Environment")).toBeInTheDocument();
    expect(screen.getByText("Stage Environment")).toBeInTheDocument();

    // (2) isRowHeader guard — jsdom structurally catches a missing/zero rowheader
    // that a plain render assertion would not (react-aria Table throws in
    // TableCollection.updateColumns without exactly one isRowHeader column).
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });

  it("hides Register Environment for a user without the register permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register environment/i })).toBeNull();
  });

  it("shows Register Environment for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogEnvironmentsRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register environment/i })).toBeInTheDocument();
  });

  // Exercises the react-aria Table isRowHeader gotcha (ADR-0084/CLAUDE.md): opening the
  // Register dialog alongside a live table is the heavier re-render that blank-pages the
  // screen when exactly-one-isRowHeader is violated. jsdom recovers silently on a light
  // render, so this must actually open the dialog, not just render the closed state.
  it("opening the Register Environment dialog does not blank-page the table (isRowHeader survives)", async () => {
    setPerms([KartovaPermissions.CatalogEnvironmentsRegister]);
    useEnvironmentsListMock.mockReturnValue(stubList({
      items: [envItem()],
    }));

    renderPage();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole("button", { name: /register environment/i }));

    expect(screen.getByRole("dialog", { name: /register environment/i })).toBeInTheDocument();
    expect(screen.getAllByRole("rowheader", { hidden: true }).length).toBeGreaterThan(0);
  });
});
