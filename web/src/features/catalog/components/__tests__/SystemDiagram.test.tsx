import { it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

const useGraphMock = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => useGraphMock(a) }));

vi.mock("@xyflow/react", () => ({
  ReactFlow: (props: {
    nodes: {
      id: string;
      type?: string;
      data: { displayName?: string; label?: string; selected?: boolean; outsideBoundary?: boolean };
    }[];
    edges: { id: string; label: string }[];
    onNodeClick?: (e: unknown, n: unknown) => void;
  }) => (
    <div data-testid="rf">
      <span data-testid="node-count">{props.nodes.length}</span>
      <span data-testid="edge-count">{props.edges.length}</span>
      {props.nodes.map((n) => (
        <button
          key={n.id}
          data-selected={n.data.selected ? "true" : "false"}
          data-node-type={n.type}
          data-outside={n.data.outsideBoundary ? "true" : "false"}
          onClick={() => props.onNodeClick?.({}, n)}
        >
          {n.data.displayName ?? n.data.label}
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

import { SystemDiagram } from "@/features/catalog/components/SystemDiagram";

const oneMemberGraph = {
  results: [
    {
      nodes: [
        { kind: "system", id: "s1", displayName: "Payments Platform", depth: 0, teamId: null, outDegree: 0, inDegree: 1 },
        { kind: "service", id: "m1", displayName: "Ledger", depth: 1, teamId: "t1", outDegree: 0, inDegree: 0 },
      ],
      edges: [{ id: "e1", source: { kind: "service", id: "m1" }, target: { kind: "system", id: "s1" }, type: "partOf" }],
      derivedEdges: [],
      truncated: false,
    },
  ],
  isLoading: false,
  isError: false,
};

const twoMemberGraph = {
  results: [
    {
      nodes: [
        { kind: "system", id: "s1", displayName: "Payments Platform", depth: 0, teamId: null, outDegree: 0, inDegree: 2 },
        { kind: "service", id: "m1", displayName: "Ledger", depth: 1, teamId: "t1", outDegree: 1, inDegree: 0 },
        { kind: "service", id: "m2", displayName: "Billing", depth: 1, teamId: "t1", outDegree: 0, inDegree: 1 },
      ],
      edges: [
        { id: "e1", source: { kind: "service", id: "m1" }, target: { kind: "system", id: "s1" }, type: "partOf" },
        { id: "e2", source: { kind: "service", id: "m2" }, target: { kind: "system", id: "s1" }, type: "partOf" },
        { id: "e3", source: { kind: "service", id: "m1" }, target: { kind: "service", id: "m2" }, type: "dependsOn" },
      ],
      derivedEdges: [],
      truncated: false,
    },
  ],
  isLoading: false,
  isError: false,
};

const memberAndNonMemberGraph = {
  results: [
    {
      nodes: [
        { kind: "system", id: "s1", displayName: "Payments Platform", depth: 0, teamId: null, outDegree: 0, inDegree: 1 },
        { kind: "service", id: "m1", displayName: "Ledger", depth: 1, teamId: "t1", outDegree: 1, inDegree: 0 },
        { kind: "service", id: "ext", displayName: "Auth Service", depth: 2, teamId: "t2", outDegree: 0, inDegree: 1 },
      ],
      edges: [
        { id: "e1", source: { kind: "service", id: "m1" }, target: { kind: "system", id: "s1" }, type: "partOf" },
        { id: "e2", source: { kind: "service", id: "m1" }, target: { kind: "service", id: "ext" }, type: "dependsOn" },
      ],
      derivedEdges: [],
      truncated: false,
    },
  ],
  isLoading: false,
  isError: false,
};

// depth-2 shape: focus s1 <- partOf m1; m1 -> ext (dependsOn); ext -> s2 (partOf, a DIFFERENT
// System). s2 is a non-member of s1 — reachable only through a non-member's own partOf edge —
// and must carry the same outside-boundary marking as `ext` (spec 7a / missing-test 4).
const memberAndSecondSystemGraph = {
  results: [
    {
      nodes: [
        { kind: "system", id: "s1", displayName: "Payments Platform", depth: 0, teamId: null, outDegree: 0, inDegree: 1 },
        { kind: "service", id: "m1", displayName: "Ledger", depth: 1, teamId: "t1", outDegree: 1, inDegree: 0 },
        { kind: "service", id: "ext", displayName: "Auth Service", depth: 2, teamId: "t2", outDegree: 1, inDegree: 1 },
        { kind: "system", id: "s2", displayName: "Other Platform", depth: 2, teamId: "t3", outDegree: 0, inDegree: 1 },
      ],
      edges: [
        { id: "e1", source: { kind: "service", id: "m1" }, target: { kind: "system", id: "s1" }, type: "partOf" },
        { id: "e2", source: { kind: "service", id: "m1" }, target: { kind: "service", id: "ext" }, type: "dependsOn" },
        { id: "e3", source: { kind: "service", id: "ext" }, target: { kind: "system", id: "s2" }, type: "partOf" },
      ],
      derivedEdges: [],
      truncated: false,
    },
  ],
  isLoading: false,
  isError: false,
};

function renderDiagram(systemId = "s1", displayName = "Payments Platform") {
  return render(
    <MemoryRouter>
      <SystemDiagram systemId={systemId} displayName={displayName} />
    </MemoryRouter>,
  );
}

it("requests depth 1 by default and depth 2 once external dependencies are included", async () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  renderDiagram();
  expect(useGraphMock).toHaveBeenLastCalledWith(expect.objectContaining({ depth: 1 }));

  await userEvent.click(screen.getByRole("switch", { name: /include external dependencies/i }));
  expect(useGraphMock).toHaveBeenLastCalledWith(expect.objectContaining({ depth: 2 }));
});

it("renders the band labelled with the system name, and the member node", () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  const { container } = renderDiagram();
  // The System's own entity node renders the same display name, so assert on the band node
  // itself (data-node-type="systemBoundary") rather than the text anywhere on the page — that
  // would pass even if the band computation returned null.
  const band = container.querySelector('button[data-node-type="systemBoundary"]');
  expect(band).not.toBeNull();
  expect(band).toHaveTextContent("Payments Platform");
  expect(screen.getByText("Ledger")).toBeInTheDocument();
});

it("does not render partOf edges", () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  renderDiagram();
  // oneMemberGraph's only edge is the member's partOf; asserting edge-count is 0 is the
  // stronger check the next test in this file already uses for dependsOn edges.
  expect(screen.getByTestId("edge-count")).toHaveTextContent("0");
});

it("renders a dependsOn edge between two members (the reason this diagram uses /graph)", () => {
  useGraphMock.mockReturnValue(twoMemberGraph);
  renderDiagram();
  // Both partOf edges are filtered out; only the member<->member dependsOn edge remains.
  expect(screen.getByTestId("edge-count")).toHaveTextContent("1");
  expect(screen.getByTestId("edge-label")).toHaveTextContent("Depends on");
});

it("does not select the boundary band on click, leaving a previously-selected member selected", () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  const { container } = renderDiagram();

  fireEvent.click(screen.getByRole("button", { name: "Ledger" }));
  expect(screen.getByRole("button", { name: "Ledger" })).toHaveAttribute("data-selected", "true");

  // Clicking the band (systemBoundary node) must early-return rather than falling through to
  // setSelectedId — otherwise it would clear the member's selection above.
  const band = container.querySelector('button[data-node-type="systemBoundary"]');
  expect(band).not.toBeNull();
  fireEvent.click(band!);
  expect(screen.getByRole("button", { name: "Ledger" })).toHaveAttribute("data-selected", "true");
});

it("shows the empty state for a system with no members", () => {
  useGraphMock.mockReturnValue({
    results: [{ nodes: [{ kind: "system", id: "s1", displayName: "Empty", depth: 0, teamId: null, outDegree: 0, inDegree: 0 }], edges: [], derivedEdges: [], truncated: false }],
    isLoading: false,
    isError: false,
  });
  renderDiagram("s1", "Empty");
  expect(screen.getByText(/no members yet/i)).toBeInTheDocument();
});

it("shows a loading skeleton while the graph query is in flight (missing test 3)", () => {
  useGraphMock.mockReturnValue({ results: [], isLoading: true, isError: false });
  const { container } = renderDiagram();
  expect(screen.getByRole("region", { name: /system diagram/i })).toBeInTheDocument();
  expect(container.querySelector(".animate-pulse")).not.toBeNull();
  expect(screen.queryByTestId("rf")).not.toBeInTheDocument();
});

it("shows an error state scoped to the diagram", () => {
  useGraphMock.mockReturnValue({ results: [], isLoading: false, isError: true });
  renderDiagram("s1", "X");
  expect(screen.getByText(/couldn.t load the system diagram/i)).toBeInTheDocument();
});

it("warns when the response was truncated", () => {
  useGraphMock.mockReturnValue({ ...oneMemberGraph, results: [{ ...oneMemberGraph.results[0]!, truncated: true }] });
  renderDiagram("s1", "X");
  expect(screen.getByText(/only part of/i)).toBeInTheDocument();
});

it("links to the full explorer focused on the system", () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  renderDiagram("s1", "X");
  expect(screen.getByRole("link", { name: /open full graph/i })).toHaveAttribute("href", "/graph?focus=system:s1");
});

it("marks a non-member outside the boundary and leaves the member unmarked (spec 7a)", () => {
  useGraphMock.mockReturnValue(memberAndNonMemberGraph);
  renderDiagram();

  expect(screen.getByRole("button", { name: "Ledger" })).toHaveAttribute("data-outside", "false");
  expect(screen.getByRole("button", { name: "Auth Service" })).toHaveAttribute("data-outside", "true");
  // The focus System node is the subject of the diagram, never marked outside. Both the band and
  // the System's own entity node render the label "Payments Platform" (the known, out-of-scope
  // duplicated label) — narrow to the entity node specifically.
  const systemNode = screen
    .getAllByRole("button", { name: "Payments Platform" })
    .find((el) => el.getAttribute("data-node-type") === "entity")!;
  expect(systemNode).toHaveAttribute("data-outside", "false");
});

it("marks a second System reached at depth 2 as a non-member, same as any other non-member (missing test 4, spec 7a)", () => {
  useGraphMock.mockReturnValue(memberAndSecondSystemGraph);
  renderDiagram();

  const secondSystemNode = screen
    .getAllByRole("button", { name: "Other Platform" })
    .find((el) => el.getAttribute("data-node-type") === "entity")!;
  expect(secondSystemNode).toHaveAttribute("data-outside", "true");
});

it("shows the outside-boundary legend row", () => {
  useGraphMock.mockReturnValue(oneMemberGraph);
  renderDiagram();
  expect(screen.getByText(/outside this system/i)).toBeInTheDocument();
});
