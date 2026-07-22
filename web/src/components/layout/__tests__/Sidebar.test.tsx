import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

// Hoisted mock — must precede the `import { Sidebar } from "../Sidebar"` below
// so the Sidebar module captures our stub instead of the real hook.
const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

import { Sidebar } from "../Sidebar";
import { KartovaPermissions } from "@/shared/auth/permissions";

type Perm = (typeof KartovaPermissions)[keyof typeof KartovaPermissions];

function setPermissions(...perms: Perm[]) {
  const set = new Set<string>(perms);
  usePermissionsMock.mockReturnValue({
    role: "test",
    hasPermission: (p: Perm) => set.has(p),
    isLoading: false,
    isError: false,
    teamIds: [],
    teamAdminTeamIds: [],
  });
}

function renderSidebar() {
  return render(
    <MemoryRouter>
      <Sidebar />
    </MemoryRouter>,
  );
}

describe("Sidebar", () => {
  beforeEach(() => {
    usePermissionsMock.mockReset();
  });

  it("always renders the Catalog group + Applications link, regardless of permissions", () => {
    setPermissions(); // no permissions at all
    renderSidebar();
    expect(screen.getByTestId("nav-group-catalog")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Applications" })).toBeInTheDocument();
  });

  it("renders the Teams link only when the user has TeamRead", () => {
    setPermissions(KartovaPermissions.TeamRead);
    renderSidebar();
    expect(screen.getByRole("link", { name: "Teams" })).toBeInTheDocument();
  });

  it("hides the Teams link when the user lacks TeamRead", () => {
    setPermissions(); // no TeamRead
    renderSidebar();
    expect(screen.queryByRole("link", { name: "Teams" })).toBeNull();
  });

  it("renders the Settings group + Organization link when the user has OrgProfileRead", () => {
    setPermissions(KartovaPermissions.OrgProfileRead);
    renderSidebar();
    expect(screen.getByTestId("nav-group-settings")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Organization" })).toBeInTheDocument();
  });

  it("hides the entire Settings group when the user lacks OrgProfileRead", () => {
    setPermissions(KartovaPermissions.TeamRead); // only Teams, no org perms
    renderSidebar();
    expect(screen.queryByTestId("nav-group-settings")).toBeNull();
    expect(screen.queryByRole("link", { name: "Organization" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Invitations" })).toBeNull();
  });

  it("renders the Invitations sub-link only when OrgInvitationsRead is also granted", () => {
    setPermissions(
      KartovaPermissions.OrgProfileRead,
      KartovaPermissions.OrgInvitationsRead,
    );
    renderSidebar();
    expect(screen.getByRole("link", { name: "Invitations" })).toBeInTheDocument();
  });

  it("hides the Invitations sub-link when only OrgProfileRead is granted", () => {
    setPermissions(KartovaPermissions.OrgProfileRead);
    renderSidebar();
    expect(screen.getByRole("link", { name: "Organization" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Invitations" })).toBeNull();
  });

  it("renders the Services link (promoted from a disabled placeholder)", () => {
    setPermissions();
    renderSidebar();
    expect(screen.getByRole("link", { name: "Services" })).toBeInTheDocument();
  });

  it("renders an APIs nav link under Catalog", () => {
    setPermissions();
    renderSidebar();
    expect(screen.getByRole("link", { name: "APIs" })).toHaveAttribute("href", "/catalog/apis");
  });

  it("renders a Systems nav link under Catalog", () => {
    setPermissions();
    renderSidebar();
    expect(screen.getByRole("link", { name: "Systems" })).toHaveAttribute("href", "/catalog/systems");
  });

  it("renders disabled placeholders (Infrastructure / Docs) with data-disabled", () => {
    setPermissions();
    renderSidebar();
    const disabled = screen.getAllByText(/Infrastructure|Docs/);
    expect(disabled.length).toBe(2);
    for (const node of disabled) {
      expect(node.getAttribute("data-disabled")).toBe("true");
    }
  });

  function renderAt(path: string) {
    return render(
      <MemoryRouter initialEntries={[path]}>
        <Sidebar />
      </MemoryRouter>,
    );
  }

  it("highlights only Services (not Applications) on /catalog/services", () => {
    setPermissions();
    renderAt("/catalog/services");
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Applications" })).not.toHaveAttribute("aria-current", "page");
  });

  it("highlights Applications on /catalog/applications and keeps it on detail routes", () => {
    setPermissions();
    const { unmount } = renderAt("/catalog/applications");
    expect(screen.getByRole("link", { name: "Applications" })).toHaveAttribute("aria-current", "page");
    unmount();

    renderAt("/catalog/applications/abc-123");
    expect(screen.getByRole("link", { name: "Applications" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Services" })).not.toHaveAttribute("aria-current", "page");
  });
});
