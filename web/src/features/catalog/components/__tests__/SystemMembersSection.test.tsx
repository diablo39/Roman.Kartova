import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const useRelationshipsListMock = vi.fn();
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: (...a: unknown[]) => useRelationshipsListMock(...a),
}));

import { SystemMembersSection } from "../SystemMembersSection";
import * as perms from "@/shared/auth/usePermissions";
import * as systems from "@/features/catalog/api/systems";

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
});
