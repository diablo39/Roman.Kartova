import { it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ApiSurfaceSection } from "@/features/catalog/components/ApiSurfaceSection";
import * as api from "@/features/catalog/api/apiSurface";
import * as rel from "@/features/catalog/api/relationships";
import * as perms from "@/shared/auth/usePermissions";

function mockSurface(data: api.ApiSurfaceResponse) {
  vi.spyOn(api, "useApiSurface").mockReturnValue({
    data,
    isLoading: false,
    isError: false,
  } as never);
}

function mockPermissions(canManage: boolean) {
  vi.spyOn(perms, "usePermissions").mockReturnValue({
    hasPermission: () => canManage,
    role: canManage ? "OrgAdmin" : "Member",
    teamIds: [],
  } as never);
}

const deleteMutate = vi.fn().mockResolvedValue(undefined);
function mockDelete() {
  vi.spyOn(rel, "useDeleteRelationship").mockReturnValue({
    mutateAsync: deleteMutate,
    isPending: false,
  } as never);
}

function renderSection(entityKind: "service" | "application", entityId: string, entityTeamId = "team1") {
  return render(
    <MemoryRouter>
      <ApiSurfaceSection entityKind={entityKind} entityId={entityId} entityTeamId={entityTeamId} />
    </MemoryRouter>,
  );
}

const derivedProvide: api.ApiSurfaceItem = {
  apiId: "a1",
  displayName: "Orders API",
  style: "rest",
  version: "v1",
  hasSpec: true,
  origin: "derived",
  viaApplicationId: "app1",
  viaApplicationDisplayName: "Billing",
  relationshipId: null,
};

const directConsume: api.ApiSurfaceItem = {
  apiId: "a2",
  displayName: "Events API",
  style: "asyncApi",
  version: "2.0",
  hasSpec: false,
  origin: "direct",
  viaApplicationId: null,
  viaApplicationDisplayName: null,
  relationshipId: "rel-consume-1",
};

beforeEach(() => {
  vi.restoreAllMocks();
  deleteMutate.mockClear();
  mockPermissions(false);
  mockDelete();
});

it("renders provided and consumed APIs, with derived-origin via-link", () => {
  mockSurface({ provides: [derivedProvide], consumes: [directConsume] });

  renderSection("service", "svc1");

  expect(screen.getByText("Orders API")).toBeInTheDocument();
  expect(screen.getByText("Events API")).toBeInTheDocument();
  // "Derived · via " and the "Billing" link are separate text nodes; match
  // the innermost <span> whose aggregated textContent contains the phrase.
  expect(
    screen.getAllByText(
      (_, element) =>
        element?.tagName.toLowerCase() === "span" &&
        /via billing/i.test(element.textContent ?? ""),
    ).length,
  ).toBeGreaterThan(0);
  // react-aria Table requires an isRowHeader column, or it throws at
  // TableCollection.updateColumns and blank-pages on a heavier render.
  expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
});

it("shows empty copy when list is empty", () => {
  mockSurface({ provides: [], consumes: [] });
  renderSection("application", "app1");
  expect(screen.getByText(/no apis provided/i)).toBeInTheDocument();
  expect(screen.getByText(/no apis consumed/i)).toBeInTheDocument();
});

it("hides Remove when the caller cannot manage", () => {
  mockPermissions(false);
  mockSurface({ provides: [], consumes: [directConsume] });
  renderSection("service", "svc1");
  expect(screen.queryByRole("button", { name: /remove/i })).not.toBeInTheDocument();
});

it("shows Remove on direct rows (not derived) when the caller can manage and deletes by relationship id", async () => {
  mockPermissions(true);
  const directProvide: api.ApiSurfaceItem = { ...derivedProvide, apiId: "a3", displayName: "Payments API", origin: "direct", viaApplicationId: null, viaApplicationDisplayName: null, relationshipId: "rel-provide-1" };
  mockSurface({ provides: [directProvide, derivedProvide], consumes: [directConsume] });
  vi.spyOn(window, "confirm").mockReturnValue(true);

  renderSection("service", "svc1");

  const removeButtons = screen.getAllByRole("button", { name: /remove/i });
  // one direct provide + one direct consume = 2 (the derived provide row has no Remove)
  expect(removeButtons).toHaveLength(2);

  await userEvent.click(removeButtons[0]!);
  expect(deleteMutate).toHaveBeenCalledWith("rel-provide-1");
});
