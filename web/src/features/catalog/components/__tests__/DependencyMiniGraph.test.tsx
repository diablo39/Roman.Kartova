import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const navigate = vi.fn();
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@xyflow/react", () => ({
  ReactFlow: (props: {
    nodes: { id: string; data: unknown }[];
    edges: { id: string; label: string }[];
    onNodeClick?: (e: unknown, n: unknown) => void;
  }) => (
    <div data-testid="rf">
      <span data-testid="node-count">{props.nodes.length}</span>
      <span data-testid="edge-count">{props.edges.length}</span>
      {props.nodes.map((n) => (
        <button
          key={n.id}
          data-selected={(n.data as { selected?: boolean }).selected ? "true" : "false"}
          onClick={() => props.onNodeClick?.({}, n)}
        >
          {(n.data as { displayName: string }).displayName}
        </button>
      ))}
      {props.edges.map((e) => (
        <span key={e.id} data-testid="edge-label">
          {e.label}
        </span>
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

it("collapses the derived label to one api name when two paths go through the same api", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([]));
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: {
      dependencies: [{
        serviceId: "s3", displayName: "PaymentsService", teamId: null,
        paths: [
          { apiId: "a1", apiName: "Orders API", viaApplicationId: null, viaApplicationDisplayName: null },
          { apiId: "a1", apiName: "Orders API", viaApplicationId: "app1", viaApplicationDisplayName: "App 1" },
        ],
      }],
      dependents: [],
    },
    isLoading: false, isError: false,
  } as never);
  renderGraph();
  // Bug fix: two paths through the same API must render "via Orders API", not "via Orders API +1".
  expect(screen.getByTestId("edge-label")).toHaveTextContent("via Orders API");
});

it("does not fetch derived dependencies for a non-service entity (ADR-0111 §5 service-only)", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([]));
  render(
    <MemoryRouter>
      <DependencyMiniGraph entityKind="application" entityId="a1" displayName="App" />
    </MemoryRouter>,
  );
  expect(derivedApi.useDerivedDependencies).toHaveBeenCalledWith("a1", { enabled: false });
});

it("shows a degradation notice when the derived-dependency fetch fails, without dropping the graph", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: undefined, isLoading: false, isError: true,
  } as never);
  renderGraph();
  expect(screen.getByTestId("rf")).toBeInTheDocument(); // persisted edges still render
  expect(screen.getByText(/derived dependencies couldn.t be loaded/i)).toBeInTheDocument();
});

it("does not show the derived-fetch notice for a non-service entity", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: undefined, isLoading: false, isError: true,
  } as never);
  render(
    <MemoryRouter>
      <DependencyMiniGraph entityKind="application" entityId="a1" displayName="App" />
    </MemoryRouter>,
  );
  expect(screen.queryByText(/derived dependencies couldn.t be loaded/i)).not.toBeInTheDocument();
});

it("selects a neighbour on node click (does not navigate); focused node is not selectable", () => {
  // Matches the /graph explorer: a bare click SELECTS/highlights the node rather than
  // navigating. Navigation moved to the node's ⋯ "Open page ↗" menu (openPage →
  // entityDetailPath, whose kind→route mapping is unit-tested in graphModel.test.ts).
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  renderGraph();
  const neighbour = screen.getByRole("button", { name: "AuthService" });
  fireEvent.click(neighbour);
  expect(navigate).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "AuthService" })).toHaveAttribute("data-selected", "true");

  // The focused node (current page's entity) stays non-selectable.
  fireEvent.click(screen.getByRole("button", { name: "Me" }));
  expect(navigate).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Me" })).toHaveAttribute("data-selected", "false");
});

it("renders an API-kind neighbour as a selectable node (routing to /catalog/apis lives in openPage → entityDetailPath)", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(
    listResult([
      {
        id: "rApi",
        type: "consumesApiFrom",
        origin: "manual",
        source: { kind: "service", id: "s1", displayName: "Me" },
        target: { kind: "api", id: "api9", displayName: "Orders API" },
        createdByUserId: "u1",
        createdAt: "2026-06-25T00:00:00Z",
      },
    ]),
  );
  renderGraph();
  fireEvent.click(screen.getByRole("button", { name: "Orders API" }));
  expect(navigate).not.toHaveBeenCalled();
  expect(screen.getByRole("button", { name: "Orders API" })).toHaveAttribute("data-selected", "true");
});
