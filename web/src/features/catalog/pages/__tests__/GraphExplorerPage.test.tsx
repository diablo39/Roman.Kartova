import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { GraphExplorerPage } from "@/features/catalog/pages/GraphExplorerPage";


const mockNavigate = vi.fn();
vi.mock("react-router-dom", async (orig) => ({ ...(await orig<typeof import("react-router-dom")>()), useNavigate: () => mockNavigate }));

const mockUseGraph = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => mockUseGraph(a) }));

// ReactFlow stub: render each node as a clickable button; capture nodes for assertions.
type RFNode = { id: string; data: { displayName: string; dimmed?: boolean } };
let _capturedNodes: RFNode[] = [];
function capturedReactFlowNodes() { return _capturedNodes; }

vi.mock("@xyflow/react", () => ({
  ReactFlow: ({ nodes, onNodeClick, children }: { nodes: RFNode[]; onNodeClick: (e: unknown, n: { id: string }) => void; children?: React.ReactNode }) => {
    _capturedNodes = nodes;
    return (
      <div data-testid="rf">
        {nodes.map((n) => (
          <button key={n.id} data-testid={`node-${n.id}`} onClick={() => onNodeClick({}, n)}>{n.data.displayName}</button>
        ))}
        {children}
      </div>
    );
  },
  Background: () => null,
  Controls: () => null,
  MiniMap: () => null,
  Panel: ({ children }: { children?: React.ReactNode }) => <div data-testid="rf-panel">{children}</div>,
}));
import React from "react";

// Sidebar stub: expose the expand callback, set-focus callback + close.
vi.mock("@/features/catalog/components/GraphExplorerSidebar", () => ({
  GraphExplorerSidebar: ({ selected, onToggleExpand, onSetFocus, onClose }: { selected: { kind: string; id: string }; onToggleExpand: (node: string, dir: "out" | "in") => void; onSetFocus: () => void; onClose: () => void }) => (
    <div data-testid="sidebar">
      <span>sidebar:{selected.kind}:{selected.id}</span>
      <button onClick={() => onToggleExpand(`${selected.kind}:${selected.id}`, "out")}>expand-out</button>
      <button onClick={onSetFocus}>set-focus</button>
      <button onClick={onClose}>close</button>
    </div>
  ),
}));

const mockUseGraphFilters = vi.fn();
vi.mock("@/features/catalog/relationships/useGraphFilters", () => ({
  useGraphFilters: (...args: unknown[]) => mockUseGraphFilters(...args),
}));

vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [] }),
}));

vi.mock("@/features/catalog/components/GraphFilterControls", () => ({
  GraphFilterControls: () => <div data-testid="graph-filter-controls" />,
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
  mockNavigate.mockClear();
  _capturedNodes = [];
  mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, expandError: false, refetch: vi.fn() });
  mockUseGraphFilters.mockReturnValue({
    filters: { kinds: [] as string[], teamIds: [] as string[] },
    setKinds: vi.fn(), setTeamIds: vi.fn(), clear: vi.fn(), isActive: false, activeCount: 0,
  });
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
    mockUseGraph.mockReturnValue({ results: [big], isLoading: false, isError: false, expandError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:n0");
    expect(screen.getByText(/large graph/i)).toBeInTheDocument();
  });

  it("shows the error banner and Try-again calls refetch on focus-fetch failure", () => {
    const refetch = vi.fn();
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: true, expandError: false, refetch });
    renderAt("/graph?focus=service:f");
    expect(screen.getByText(/couldn't load/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /try again/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it("shows expand-error notice non-blockingly while graph nodes still render", () => {
    mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, expandError: true, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    expect(screen.getByText(/some expansions failed/i)).toBeInTheDocument();
    expect(screen.getByTestId("node-service:a")).toBeInTheDocument();
  });

  it("set-as-focus navigates to the selected node's focus URL", () => {
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    fireEvent.click(screen.getByRole("button", { name: /set-focus/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/graph?focus=service:a");
  });

  it("dims nodes that don't match the active kind filter, never the focus", () => {
    // Arrange: a graph with the focus (application) + one service neighbour, and an
    // active Kind=application filter.
    mockUseGraph.mockReturnValue({
      results: [
        {
          truncated: false,
          nodes: [
            { kind: "application", id: "focus", displayName: "Focus", depth: 0, teamId: "t1" },
            { kind: "service", id: "s1", displayName: "Svc 1", depth: 1, teamId: "t1" },
          ],
          edges: [{ id: "e1", source: { kind: "application", id: "focus" }, target: { kind: "service", id: "s1" }, type: "DependsOn", origin: "Manual" }],
        },
      ],
      isLoading: false, isError: false, expandError: false, refetch: vi.fn(),
    });
    mockUseGraphFilters.mockReturnValue({
      filters: { kinds: ["application"], teamIds: [] as string[] },
      setKinds: vi.fn(), setTeamIds: vi.fn(), clear: vi.fn(), isActive: true, activeCount: 1,
    });

    renderAt("/graph?focus=application:focus");

    const passed = capturedReactFlowNodes();
    expect(passed.find((n) => n.id === "service:s1")?.data.dimmed).toBe(true);
    expect(passed.find((n) => n.id === "application:focus")?.data.dimmed).toBe(false);
  });
});