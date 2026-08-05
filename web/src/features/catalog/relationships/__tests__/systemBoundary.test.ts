import { describe, it, expect } from "vitest";
import type { Node } from "@xyflow/react";
import { systemMemberIds, systemBoundaryBox, BOUNDARY_PADDING } from "../systemBoundary";
import type { ExplorerGraph } from "../graphMerge";
import type { GraphNodeData } from "../graphModel";

const FOCUS = "system:s1";

function graph(edges: { id: string; source: string; target: string; type?: string }[]): ExplorerGraph {
  return { nodes: [], edges: edges.map((e) => ({ ...e, label: "x" })), truncated: false } as ExplorerGraph;
}

function node(id: string, x: number, y: number): Node<GraphNodeData> {
  return {
    id,
    type: "entity",
    position: { x, y },
    data: { kind: "service", entityId: id, displayName: id, side: "dependency" },
  } as Node<GraphNodeData>;
}

describe("systemMemberIds", () => {
  it("treats the source of a partOf edge pointing at the focus system as a member", () => {
    const g = graph([{ id: "e1", source: "service:m1", target: FOCUS, type: "partOf" }]);
    expect([...systemMemberIds(g, FOCUS)]).toEqual(["service:m1"]);
  });

  it("ignores a partOf edge pointing at a different system", () => {
    const g = graph([{ id: "e1", source: "service:x", target: "system:other", type: "partOf" }]);
    expect(systemMemberIds(g, FOCUS).size).toBe(0);
  });

  it("ignores a non-partOf edge into the focus", () => {
    const g = graph([{ id: "e1", source: "service:x", target: FOCUS, type: "dependsOn" }]);
    expect(systemMemberIds(g, FOCUS).size).toBe(0);
  });

  it("ignores the reversed direction (system as source)", () => {
    const g = graph([{ id: "e1", source: FOCUS, target: "service:m1", type: "partOf" }]);
    expect(systemMemberIds(g, FOCUS).size).toBe(0);
  });
});

describe("systemBoundaryBox", () => {
  it("covers the focus and its members, padded, and excludes an external node", () => {
    const nodes = [node(FOCUS, 0, 0), node("service:m1", 0, 100), node("service:ext", 500, 100)];
    const box = systemBoundaryBox(nodes, new Set(["service:m1"]), FOCUS)!;

    expect(box.x).toBe(0 - BOUNDARY_PADDING);
    expect(box.y).toBe(0 - BOUNDARY_PADDING);
    // members span y 0..100 plus node height; the external node at x=500 must not widen the box
    expect(box.x + box.width).toBeLessThan(500);
  });

  it("returns null for a system with no members", () => {
    expect(systemBoundaryBox([node(FOCUS, 0, 0)], new Set(), FOCUS)).toBeNull();
  });
});
