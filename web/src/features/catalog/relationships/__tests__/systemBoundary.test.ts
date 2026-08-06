import { describe, it, expect } from "vitest";
import type { Node } from "@xyflow/react";
import { systemMemberIds, systemBoundaryBox, BOUNDARY_PADDING, PART_OF_TYPE } from "../systemBoundary";
import { NODE_W, NODE_H } from "../graphLayout";
import type { ExplorerGraph, WireRelationshipType } from "../graphMerge";
import type { GraphNodeData } from "../graphModel";

const FOCUS = "system:s1";

function graph(edges: { id: string; source: string; target: string; type?: WireRelationshipType }[]): ExplorerGraph {
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

  it("still counts a member that also has a partOf edge to a second system (reachable at depth 2)", () => {
    const g = graph([
      { id: "e1", source: "service:m1", target: FOCUS, type: PART_OF_TYPE },
      { id: "e2", source: "service:m1", target: "system:other", type: PART_OF_TYPE },
    ]);
    expect([...systemMemberIds(g, FOCUS)]).toEqual(["service:m1"]);
  });
});

describe("systemBoundaryBox", () => {
  it("covers the members only, padded, and excludes an external node", () => {
    const nodes = [node(FOCUS, 0, 0), node("service:m1", 0, 100), node("service:ext", 500, 100)];
    const box = systemBoundaryBox(nodes, new Set(["service:m1"]), FOCUS)!;

    // Extent is over service:m1 alone (0, 100) — the focus at (0, 0) is excluded (spec 4a).
    expect(box.x).toBe(0 - BOUNDARY_PADDING);
    expect(box.y).toBe(100 - BOUNDARY_PADDING);
    // the external node at x=500 must not widen the box
    expect(box.x + box.width).toBeLessThan(500);
    // Exact size, expressed in the same constants layoutGraph/systemBoundaryBox use — mutating
    // NODE_W/NODE_H/BOUNDARY_PADDING's contribution to width/height fails this test.
    expect(box.width).toBe(NODE_W + BOUNDARY_PADDING * 2);
    expect(box.height).toBe(NODE_H + BOUNDARY_PADDING * 2);
  });

  it("stops at the last member and excludes the focus even when the focus sits far to the right", () => {
    // dagre's rankdir:"LR" puts the System to the right of every member (spec 4a's rationale);
    // this reproduces that shape directly rather than relying on it falling out of dagre.
    const nodes = [node("service:m1", 0, 0), node("service:m2", 200, 0), node(FOCUS, 1000, 0)];
    const box = systemBoundaryBox(nodes, new Set(["service:m1", "service:m2"]), FOCUS)!;

    const lastMemberRightEdge = 200 + NODE_W;
    expect(box.x + box.width).toBe(lastMemberRightEdge + BOUNDARY_PADDING);
    expect(box.x + box.width).toBeLessThan(1000);
  });

  it("returns null for a system with no members", () => {
    expect(systemBoundaryBox([node(FOCUS, 0, 0)], new Set(), FOCUS)).toBeNull();
  });

  it("returns null when memberIds is non-empty but matches no node in the nodes array", () => {
    // Distinguishes this guard from the memberIds.size === 0 case above: here membership is
    // non-empty, but no member id is present among `nodes` (and the focus is excluded anyway).
    const nodes = [node("service:unrelated", 0, 0)];
    expect(systemBoundaryBox(nodes, new Set(["service:ghost"]), FOCUS)).toBeNull();
  });
});
