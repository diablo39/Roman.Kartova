import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Routes, Route, useSearchParams } from "react-router-dom";
import { GraphExplorerPage } from "@/features/catalog/pages/GraphExplorerPage";

const mockUseGraph = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => mockUseGraph(a) }));

// Render React Flow as a div exposing nodes + a click hook so we can drive onNodeClick.
vi.mock("@xyflow/react", () => ({
  ReactFlow: ({ nodes, onNodeClick }: any) => (
    <div data-testid="rf">
      {nodes.map((n: any) => (
        <button key={n.id} data-testid={`node-${n.id}`} onClick={() => onNodeClick({}, n)}>
          {n.data.displayName}
        </button>
      ))}
    </div>
  ),
  Background: () => null,
  Controls: () => null,
  MiniMap: () => null,
}));

function ExpandProbe() {
  const [params] = useSearchParams();
  return <div data-testid="expand">{params.get("expand") ?? ""}</div>;
}

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
      <Routes>
        <Route path="/graph" element={<><GraphExplorerPage /><ExpandProbe /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("GraphExplorerPage", () => {
  beforeEach(() => mockUseGraph.mockReset());

  it("renders focus + neighbour nodes", () => {
    mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    expect(screen.getByTestId("node-service:f")).toBeInTheDocument();
    expect(screen.getByTestId("node-service:a")).toBeInTheDocument();
  });

  it("clicking a non-focus node adds it to ?expand", () => {
    mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    expect(screen.getByTestId("expand").textContent).toContain("service:a");
  });

  it("shows an error state", () => {
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: true, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    expect(screen.getByText(/couldn.t load/i)).toBeInTheDocument();
  });

  it("clicking Try again in error state calls refetch", () => {
    const refetch = vi.fn();
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: true, refetch });
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByRole("button", { name: /try again/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it("prompts when focus is missing", () => {
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph");
    expect(screen.getByText(/pick an entity/i)).toBeInTheDocument();
  });
});
