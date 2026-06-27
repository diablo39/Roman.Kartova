import { describe, it, expect } from "vitest";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const node = (id: string, displayName: string, depth: number) =>
  ({ kind: "service", id, displayName, depth, teamId: null }) as GraphResponse["nodes"][number];
const edge = (id: string, s: string, t: string) =>
  ({ id, source: { kind: "service", id: s }, target: { kind: "service", id: t }, type: "dependsOn", origin: "manual" }) as GraphResponse["edges"][number];

describe("mergeGraphs", () => {
  it("maps one response to nodes keyed by kind:id with labelled edges", () => {
    const r: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false };
    const g = mergeGraphs([r]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:f"]);
    expect(g.edges).toEqual([{ id: "e1", source: "service:f", target: "service:a", label: "Depends on" }]);
    expect(g.truncated).toBe(false);
  });

  it("dedupes a node and an edge that appear in two responses", () => {
    const r1: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false };
    const r2: GraphResponse = { nodes: [node("a", "A", 0), node("b", "B", 1)], edges: [edge("e1", "f", "a"), edge("e2", "a", "b")], truncated: false };
    const g = mergeGraphs([r1, r2]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:b", "service:f"]);
    expect(g.edges.map((e) => e.id).sort()).toEqual(["e1", "e2"]);
  });

  it("ORs the truncated flag across responses", () => {
    expect(mergeGraphs([{ nodes: [], edges: [], truncated: false }, { nodes: [], edges: [], truncated: true }]).truncated).toBe(true);
  });

  it("unions an outgoing result and an incoming result for the same node", () => {
    const out: GraphResponse = { nodes: [node("a","A",0), node("b","B",1)], edges: [edge("e1","a","b")], truncated: false };
    const inc: GraphResponse = { nodes: [node("a","A",0), node("c","C",1)], edges: [edge("e2","c","a")], truncated: false };
    const g = mergeGraphs([out, inc]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a","service:b","service:c"]);
    expect(g.edges.map((e) => e.id).sort()).toEqual(["e1","e2"]);
  });
});

import { bfsDepth } from "@/features/catalog/relationships/graphMerge";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

describe("bfsDepth", () => {
  const g: ExplorerGraph = {
    nodes: [
      { id: "service:f", kind: "service", entityId: "f", displayName: "F" },
      { id: "service:a", kind: "service", entityId: "a", displayName: "A" },
      { id: "service:b", kind: "service", entityId: "b", displayName: "B" },
      { id: "service:x", kind: "service", entityId: "x", displayName: "X" },
    ],
    edges: [
      { id: "e1", source: "service:f", target: "service:a", label: "Depends on" },
      { id: "e2", source: "service:a", target: "service:b", label: "Depends on" },
    ],
    truncated: false,
  };

  it("returns 0 for the focus itself", () => { expect(bfsDepth(g, "service:f", "service:f")).toBe(0); });
  it("counts undirected hops", () => {
    expect(bfsDepth(g, "service:f", "service:a")).toBe(1);
    expect(bfsDepth(g, "service:f", "service:b")).toBe(2);
  });
  it("returns null for an unreachable node", () => { expect(bfsDepth(g, "service:f", "service:x")).toBeNull(); });
});
