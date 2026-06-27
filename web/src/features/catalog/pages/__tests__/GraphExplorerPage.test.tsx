import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { GraphExplorerPage } from "@/features/catalog/pages/GraphExplorerPage";

const mockUseGraph = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => mockUseGraph(a) }));

// ReactFlow stub: render each node as a clickable button.
vi.mock("@xyflow/react", () => ({
  ReactFlow: ({ nodes, onNodeClick }: any) => (
    <div data-testid="rf">
      {nodes.map((n: any) => (
        <button key={n.id} data-testid={`node-${n.id}`} onClick={() => onNodeClick({}, n)}>{n.data.displayName}</button>
      ))}
    </div>
  ),
  Background: () => null, Controls: () => null, MiniMap: () => null,
}));
// Sidebar stub: expose the expand callback + close.
vi.mock("@/features/catalog/components/GraphExplorerSidebar", () => ({
  GraphExplorerSidebar: ({ selected, onToggleExpand, onClose }: any) => (
    <div data-testid="sidebar">
      <span>sidebar:{selected.kind}:{selected.id}</span>
      <button onClick={() => onToggleExpand(`${selected.kind}:${selected.id}`, "out")}>expand-out</button>
      <button onClick={onClose}>close</button>
    </div>
  ),
}));

const result = {
  nodes: [
    { kind: "service", id: "f", displayName: "Focus", depth: 0, teamId: null },
    { kind: "service", id: "a", displayName: "A", depth: 1, teamId: null },
  ],
  edges: [{ id: "e1", source: { kind: "service", id: "f" }, target: { kind: "service", id: "a" }, type: "dependsOn", origin: "manual" }],
  truncated: false,
};

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes><Route path="/graph" element={<GraphExplorerPage />} /></Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  sessionStorage.clear();
  mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("GraphExplorerPage", () => {
  it("renders nodes and opens the sidebar on node click", () => {
    renderAt("/graph?focus=service:f");
    expect(screen.getByTestId("node-service:a")).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar")).toBeNull();
    fireEvent.click(screen.getByTestId("node-service:a"));
    expect(screen.getByText("sidebar:service:a")).toBeInTheDocument();
  });

  it("directional expand from the sidebar updates useGraph's expand arg", () => {
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    fireEvent.click(screen.getByText("expand-out"));
    // last useGraph call received the new expand entry
    const lastArg = mockUseGraph.mock.calls.at(-1)![0];
    expect(lastArg.expand).toContainEqual({ node: "service:a", dir: "out" });
  });

  it("Reset clears the sidebar selection", () => {
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    expect(screen.queryByTestId("sidebar")).toBeNull();
  });

  it("shows the missing-focus prompt", () => {
    renderAt("/graph");
    expect(screen.getByText(/pick an entity/i)).toBeInTheDocument();
  });

  it("shows the cap notice when nodes exceed the soft cap", () => {
    const big = { nodes: Array.from({ length: 151 }, (_, i) => ({ kind: "service", id: `n${i}`, displayName: `N${i}`, depth: 1, teamId: null })), edges: [], truncated: false };
    mockUseGraph.mockReturnValue({ results: [big], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:n0");
    expect(screen.getByText(/large graph/i)).toBeInTheDocument();
  });
});