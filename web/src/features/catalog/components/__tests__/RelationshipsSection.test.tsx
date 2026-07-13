import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
import * as api from "@/features/catalog/api/relationships";
import * as perms from "@/shared/auth/usePermissions";

function listResult(items: Partial<api.RelationshipResponse>[]) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() } as never;
}
const out: Partial<api.RelationshipResponse>[] = [{ id: "r1", type: "dependsOn", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }];
const inc: Partial<api.RelationshipResponse>[] = [{ id: "r2", type: "dependsOn", origin: "manual", source: { kind: "application", id: "a1", displayName: "Checkout" }, target: { kind: "service", id: "s1", displayName: "Me" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }];

function mockLists() {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "outgoing" ? out : inc));
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
}
function mockPerms(can: boolean) {
  vi.spyOn(perms, "usePermissions").mockReturnValue({
    hasPermission: () => can, role: can ? "OrgAdmin" : "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
  } as never);
}
function renderSection() {
  return render(
    <MemoryRouter>
      <RelationshipsSection entityKind="service" entityId="s1" entityTeamId="t1" entityDisplayName="Me" />
    </MemoryRouter>,
  );
}
beforeEach(() => vi.restoreAllMocks());

it("renders the dependency target link and the dependent source link", () => {
  mockLists(); mockPerms(true);
  renderSection();
  expect(screen.getByText("AuthService").closest("a")).toHaveAttribute("href", "/catalog/services/s2"); // outgoing → target
  expect(screen.getByText("Checkout").closest("a")).toHaveAttribute("href", "/catalog/applications/a1"); // incoming → source
  expect(screen.getAllByText("Manual").length).toBeGreaterThan(0);
  // Regression: each table must designate an isRowHeader column, or react-aria
  // throws "A table must have at least one Column with isRowHeader" and blanks the page.
  expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
});

it("hides Add and Delete when the user cannot manage", () => {
  mockLists(); mockPerms(false);
  renderSection();
  expect(screen.queryByRole("button", { name: /add outgoing/i })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
});

it("deletes a row after confirm", async () => {
  mockLists(); mockPerms(true);
  const mutateAsync = vi.fn().mockResolvedValue(undefined);
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  vi.spyOn(window, "confirm").mockReturnValue(true);
  renderSection();
  fireEvent.click(screen.getAllByRole("button", { name: /delete/i })[0]!);
  await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith("r1"));
});

it("renders api-target rows with a link to the API detail page", () => {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "outgoing"
      ? [{ id: "r3", type: "providesApiFor", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "api", id: "api-1", displayName: "Orders API" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }]
      : []));
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  renderSection();
  expect(screen.getByText("Orders API").closest("a")).toHaveAttribute("href", "/catalog/apis/api-1");
  expect(screen.getByText("Provides API for")).toBeInTheDocument();
});

it("incoming-only variant hides Outgoing group and disables add/delete", () => {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "incoming"
      ? [{ id: "r4", type: "consumesApiFrom", origin: "manual",
          source: { kind: "service", id: "s2", displayName: "Billing" },
          target: { kind: "api", id: "api-1", displayName: "Orders API" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }]
      : []));
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  render(
    <MemoryRouter>
      <RelationshipsSection entityKind="api" entityId="api-1" entityTeamId="t1" entityDisplayName="Orders API" variant="incoming-only" />
    </MemoryRouter>,
  );
  expect(screen.queryByText("Outgoing")).not.toBeInTheDocument();
  expect(screen.getByText("Incoming")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /add/i })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  expect(screen.getByText("Billing").closest("a")).toHaveAttribute("href", "/catalog/services/s2");
  expect(screen.getByText("Consumes API from")).toBeInTheDocument();
});

it("full variant requests relationships with excludeApiEdges (slice #71)", () => {
  const calls: api.RelationshipsListParams[] = [];
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) => {
    calls.push(p);
    return listResult(p.direction === "outgoing" ? out : inc);
  });
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  renderSection();
  const outgoingCall = calls.find((c) => c.direction === "outgoing");
  const incomingCall = calls.find((c) => c.direction === "incoming");
  expect(outgoingCall?.excludeApiEdges).toBe(true);
  expect(incomingCall?.excludeApiEdges).toBe(true);
});

it("incoming-only variant does not set excludeApiEdges (slice #71)", () => {
  const calls: api.RelationshipsListParams[] = [];
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) => {
    calls.push(p);
    return listResult(p.direction === "incoming" ? inc : []);
  });
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  render(
    <MemoryRouter>
      <RelationshipsSection entityKind="api" entityId="api-1" entityTeamId="t1" entityDisplayName="Orders API" variant="incoming-only" />
    </MemoryRouter>,
  );
  const incomingCall = calls.find((c) => c.direction === "incoming");
  expect(incomingCall?.excludeApiEdges).toBe(false);
});
