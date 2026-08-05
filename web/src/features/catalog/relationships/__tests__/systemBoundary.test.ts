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
  it("covers the focus and its members, padded, and excludes an external node", () => {
    const nodes = [node(FOCUS, 0, 0), node("service:m1", 0, 100), node("service:ext", 500, 100)];
    const box = systemBoundaryBox(nodes, new Set(["service:m1"]), FOCUS)!;

    expect(box.x).toBe(0 - BOUNDARY_PADDING);
    expect(box.y).toBe(0 - BOUNDARY_PADDING);
    // members span y 0..100 plus node height; the external node at x=500 must not widen the box
    expect(box.x + box.width).toBeLessThan(500);
    // Exact size, expressed in the same constants layoutGraph/systemBoundaryBox use — mutating
    // NODE_W/NODE_H/BOUNDARY_PADDING's contribution to width/height fails this test.
    expect(box.width).toBe(NODE_W + BOUNDARY_PADDING * 2);
    expect(box.height).toBe(100 + NODE_H + BOUNDARY_PADDING * 2);
  });

  it("returns null for a system with no members", () => {
    expect(systemBoundaryBox([node(FOCUS, 0, 0)], new Set(), FOCUS)).toBeNull();
  });

  it("returns null when memberIds is non-empty but matches no node in the nodes array", () => {
    // Distinguishes this guard from the memberIds.size === 0 case above: here membership is
    // non-empty, but neither the focus node nor any member id is present among `nodes`.
    const nodes = [node("service:unrelated", 0, 0)];
    expect(systemBoundaryBox(nodes, new Set(["service:ghost"]), FOCUS)).toBeNull();
  });
});
