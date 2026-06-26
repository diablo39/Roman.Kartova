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
});
