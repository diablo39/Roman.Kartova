import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const navigate = vi.fn();
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@xyflow/react", () => ({
  ReactFlow: (props: { nodes: { id: string; data: unknown }[]; edges: unknown[]; onNodeClick?: (e: unknown, n: unknown) => void }) => (
    <div data-testid="rf">
      <span data-testid="node-count">{props.nodes.length}</span>
      <span data-testid="edge-count">{props.edges.length}</span>
      {props.nodes.map((n) => (
        <button key={n.id} onClick={() => props.onNodeClick?.({}, n)}>
          {(n.data as { displayName: string }).displayName}
        </button>
      ))}
    </div>
  ),
  Background: () => null,
}));

import { DependencyMiniGraph } from "@/features/catalog/components/DependencyMiniGraph";
import * as api from "@/features/catalog/api/relationships";
import * as derivedApi from "@/features/catalog/api/derivedDependencies";

function listResult(items: Partial<api.RelationshipResponse>[], extra: Record<string, unknown> = {}) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), ...extra } as never;
}

const outgoing: Partial<api.RelationshipResponse>[] = [
  { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" },
];

function renderGraph() {
  return render(
    <MemoryRouter>
      <DependencyMiniGraph entityKind="service" entityId="s1" displayName="Me" />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.restoreAllMocks();
  navigate.mockReset();
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: undefined, isLoading: false, isError: false,
  } as never);
});

it("renders nodes and edges from the relationship list", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  renderGraph();
  expect(screen.getByTestId("node-count")).toHaveTextContent("2"); // focused + 1 neighbour
  expect(screen.getByTestId("edge-count")).toHaveTextContent("1");
});

it("shows an empty placeholder when there are no relationships", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([]));
  renderGraph();
  expect(screen.getByText(/no dependencies yet/i)).toBeInTheDocument();
  expect(screen.queryByTestId("rf")).not.toBeInTheDocument();
});

it("shows an error message when the list fails", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([], { isError: true }));
  renderGraph();
  expect(screen.getByText(/couldn.t load the dependency graph/i)).toBeInTheDocument();
});

it("shows an overflow note when more relationships exist", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing, { hasNext: true }));
  renderGraph();
  expect(screen.getByText(/see the tables below/i)).toBeInTheDocument();
});

it("merges derived dependency as an extra dashed edge", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing)); // focused + 1 persisted neighbour
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: {
      dependencies: [{
        serviceId: "s3", displayName: "PaymentsService", teamId: null,
        paths: [{ apiId: "a1", apiName: "Orders API", viaApplicationId: null, viaApplicationDisplayName: null }],
      }],
      dependents: [],
    },
    isLoading: false, isError: false,
  } as never);
  renderGraph();
  expect(screen.getByTestId("node-count")).toHaveTextContent("3"); // focused + persisted + derived neighbour
  expect(screen.getByTestId("edge-count")).toHaveTextContent("2"); // 1 persisted + 1 derived
});

it("navigates to a neighbour on node click but not for the focused node", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  renderGraph();
  fireEvent.click(screen.getByRole("button", { name: "AuthService" }));
  expect(navigate).toHaveBeenCalledWith("/catalog/services/s2");
  navigate.mockReset();
  fireEvent.click(screen.getByRole("button", { name: "Me" })); // focused node
  expect(navigate).not.toHaveBeenCalled();
});
