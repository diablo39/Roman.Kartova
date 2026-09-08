import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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
    sessionStorage.clear();
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

  it("renders the Catalog Hierarchy link", () => {
    setPermissions();
    renderSidebar();
    const link = screen.getByRole("link", { name: "Hierarchy" });
    expect(link).toHaveAttribute("href", "/catalog/hierarchy");
  });

  it("renders Docs as the only top-level disabled placeholder", () => {
    setPermissions();
    renderSidebar();
    const docs = screen.getByText("Docs");
    expect(docs.getAttribute("data-disabled")).toBe("true");
    // The old top-level disabled "Infrastructure" leaf is gone — it is now a
    // collapsible group header (a button), not a disabled placeholder.
    expect(screen.queryByRole("button", { name: /^Infrastructure$/i })).toBeInTheDocument();
  });

  it("groups the software entities under a collapsible Software group, expanded by default", () => {
    setPermissions();
    renderSidebar();
    const header = screen.getByRole("button", { name: /^Software$/i });
    expect(header).toHaveAttribute("aria-expanded", "true");
    for (const name of ["Applications", "Services", "APIs", "Systems"]) {
      expect(screen.getByRole("link", { name })).toBeInTheDocument();
    }
  });

  it("renders a collapsible Infrastructure group: live Virtual Machines + All Objects, disabled Brokers", () => {
    setPermissions();
    renderSidebar();
    const header = screen.getByRole("button", { name: /^Infrastructure$/i });
    expect(header).toHaveAttribute("aria-expanded", "true");
    // Virtual Machines and All Objects are live links (the old "Components" disabled stub is gone).
    expect(screen.getByRole("link", { name: "Virtual Machines" })).toHaveAttribute(
      "href",
      "/catalog/infrastructure/vms",
    );
    expect(screen.getByRole("link", { name: "All Objects" })).toHaveAttribute(
      "href",
      "/catalog/infrastructure",
    );
    // Brokers remains a disabled placeholder.
    expect(screen.getByText("Brokers").getAttribute("data-disabled")).toBe("true");
    expect(screen.queryByText("Components")).toBeNull();
  });

  it("collapsing the Software group hides its member links", () => {
    setPermissions();
    renderSidebar();
    const header = screen.getByRole("button", { name: /^Software$/i });
    fireEvent.click(header);
    expect(header).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: "Applications" })).toBeNull();
  });

  it("persists a collapsed group to sessionStorage", () => {
    setPermissions();
    renderSidebar();
    fireEvent.click(screen.getByRole("button", { name: /^Software$/i }));
    expect(sessionStorage.getItem("nav.group.software")).toBe("false");
  });

  it("restores a collapsed group from sessionStorage on mount", () => {
    sessionStorage.setItem("nav.group.software", "false");
    setPermissions();
    renderSidebar();
    expect(screen.getByRole("button", { name: /^Software$/i })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: "Applications" })).toBeNull();
  });

  it("links the group toggle to its list via aria-controls", () => {
    setPermissions();
    renderSidebar();
    const btn = screen.getByRole("button", { name: /^Software$/i });
    const controls = btn.getAttribute("aria-controls");
    expect(controls).toBeTruthy();
    expect(document.getElementById(controls!)).toBeInTheDocument();
  });

  it("defaults open and still toggles when sessionStorage throws", () => {
    const getSpy = vi
      .spyOn(Storage.prototype, "getItem")
      .mockImplementation(() => {
        throw new Error("storage blocked");
      });
    const setSpy = vi
      .spyOn(Storage.prototype, "setItem")
      .mockImplementation(() => {
        throw new Error("storage blocked");
      });
    try {
      setPermissions();
      renderSidebar();
      const btn = screen.getByRole("button", { name: /^Software$/i });
      // read threw → falls back to default-open
      expect(btn).toHaveAttribute("aria-expanded", "true");
      // write throws → state still flips, persistence silently skipped
      fireEvent.click(btn);
      expect(btn).toHaveAttribute("aria-expanded", "false");
    } finally {
      getSpy.mockRestore();
      setSpy.mockRestore();
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
