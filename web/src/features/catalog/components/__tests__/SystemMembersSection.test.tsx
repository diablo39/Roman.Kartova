import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const useRelationshipsListMock = vi.fn();
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: (...a: unknown[]) => useRelationshipsListMock(...a),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { SystemMembersSection } from "../SystemMembersSection";
import * as perms from "@/shared/auth/usePermissions";
import * as systems from "@/features/catalog/api/systems";
import { toast } from "sonner";

const edge = (kind: string, id: string, displayName: string, type = "partOf") => ({
  id: `rel-${id}`,
  type,
  origin: "manual",
  createdByUserId: "u1",
  createdAt: "2026-07-22T00:00:00Z",
  createdBy: null,
  source: { kind, id, displayName },
  target: { kind: "system", id: "sys1", displayName: "Payments" },
});

function result(over: Record<string, unknown> = {}) {
  return {
    items: [],
    isLoading: false,
    isError: false,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    ...over,
  };
}

const render1 = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("SystemMembersSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(perms, "usePermissions").mockReturnValue({
      hasPermission: () => true, role: "OrgAdmin", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
    } as never);
    // The section now unconditionally calls useSetComponentSystem (for Remove); render1 has no
    // QueryClientProvider, so the real hook (useMutation → useQueryClient) would throw. Default
    // to a no-op double here, overridden in the tests that assert on the mutation call.
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({ systemId: null, systemDisplayName: null }),
      isPending: false,
    } as never);
  });

  it("queries the incoming System relationships", () => {
    useRelationshipsListMock.mockReturnValue(result());
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(useRelationshipsListMock).toHaveBeenCalledWith(
      expect.objectContaining({ entityKind: "system", entityId: "sys1", direction: "incoming" }),
    );
  });

  it("lists member components with a kind badge + link (row header present)", () => {
    useRelationshipsListMock.mockReturnValue(
      result({
        items: [edge("application", "a1", "Billing App"), edge("service", "s1", "Ledger Svc")],
      }),
    );
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(screen.getByRole("link", { name: "Billing App" })).toHaveAttribute(
      "href",
      "/catalog/applications/a1",
    );
    expect(screen.getByRole("link", { name: "Ledger Svc" })).toHaveAttribute(
      "href",
      "/catalog/services/s1",
    );
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084
  });

  it("filters out non-PartOf edges (read-path drift tolerance)", () => {
    useRelationshipsListMock.mockReturnValue(
      result({
        items: [
          edge("application", "a1", "Billing App"),
          edge("service", "s2", "Rogue", "dependsOn"),
        ],
      }),
    );
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(screen.getByText("Billing App")).toBeInTheDocument();
    expect(screen.queryByText("Rogue")).not.toBeInTheDocument();
  });

  it("shows an empty state when nothing is assigned", () => {
    useRelationshipsListMock.mockReturnValue(result({ items: [] }));
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(screen.getByText("No components assigned yet.")).toBeInTheDocument();
  });

  it("shows a loading skeleton (with a row header)", () => {
    useRelationshipsListMock.mockReturnValue(result({ isLoading: true }));
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084 loading branch
  });

  it("shows an error line on failure", () => {
    useRelationshipsListMock.mockReturnValue(result({ isError: true }));
    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);
    expect(screen.getByText(/Couldn.t load members/i)).toBeInTheDocument();
  });

  it("removes a member with the row action", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({ systemId: null, systemDisplayName: null });
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(true); // same as RelationshipsSection.test.tsx:55
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: null }),
    );
  });

  it("hides Assign and Remove when the user cannot manage relationships", () => {
    vi.spyOn(perms, "usePermissions").mockReturnValue({
      hasPermission: () => false, role: "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
    } as never);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    expect(screen.queryByRole("button", { name: /assign component/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /remove/i })).toBeNull();
  });

  it("does not remove when the confirm dialog is cancelled", () => {
    const mutateAsync = vi.fn();
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(false);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("toasts the ProblemDetails message (via toastProblem) and does not toast success when Remove fails", async () => {
    // Now routed through toastProblem (finding: SystemMembersSection's bare catch discarded
    // every failure mode of the same useSetComponentSystem mutation the dialogs discriminate).
    // A rejection carrying `detail` surfaces that text, not the generic fallback string.
    const mutateAsync = vi.fn().mockRejectedValue({ title: "Conflict", detail: "Could not remove the member." });
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Could not remove the member."));
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("toasts the fallback wording when Remove fails with no type/detail/title", async () => {
    const mutateAsync = vi.fn().mockRejectedValue({});
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Failed to remove the component."));
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("toasts the specific concurrent-move message when Remove hits component-already-in-system", async () => {
    const mutateAsync = vi.fn().mockRejectedValue({
      type: "component-already-in-system",
      title: "Conflict",
      detail: "generic server detail that should be shadowed by the specific message",
    });
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "Someone else just assigned this component to a System. Refresh and try again.",
      ),
    );
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("toasts the 403 permission message when Remove fails with status 403", async () => {
    const mutateAsync = vi.fn().mockRejectedValue({ __status: 403 });
    vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "You can only move a component out of a System your team stewards.",
      ),
    );
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("shows Assign/Remove for a team member (not OrgAdmin) whose team matches systemTeamId", () => {
    // Pins the `teamIds.includes(...)` half of `canManage` — every other positive test in this
    // file uses role: "OrgAdmin", teamIds: [], so `role === "OrgAdmin" || teamIds.includes(...)`
    // mutated down to `role === "OrgAdmin"` would still pass them, silently shipping "team members
    // lose the button". This test's role is "Member" and only the team-id membership can allow it.
    vi.spyOn(perms, "usePermissions").mockReturnValue({
      hasPermission: () => true, role: "Member", teamIds: ["t1"], teamAdminTeamIds: [], isLoading: false, isError: false,
    } as never);
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App")] }));

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    expect(screen.getByRole("button", { name: /assign component/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /remove/i })).toBeInTheDocument();
  });

  it("does not offer Remove on a drift row whose kind is not Application/Service", () => {
    useRelationshipsListMock.mockReturnValue(
      result({
        items: [edge("application", "a1", "Billing App"), edge("api", "api1", "Orders API")],
      }),
    );

    render1(<SystemMembersSection systemId="sys1" systemTeamId="t1" systemDisplayName="Payments" />);

    // Both rows render (read-path drift tolerance — see the module docblock), but only the
    // Application/Service row gets a Remove control; `asComponentKind` exists precisely to
    // reject the `api` row rather than drive the setter with a kind it does not accept.
    expect(screen.getByText("Billing App")).toBeInTheDocument();
    expect(screen.getByText("Orders API")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /remove/i })).toHaveLength(1);
  });
});
