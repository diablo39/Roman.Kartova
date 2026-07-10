import { describe, it, expect } from "vitest";
import { buildTierMap, impactDim, tierCounts, impactTotal } from "@/features/catalog/relationships/impactModel";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const impact: GraphResponse = {
  nodes: [
    { kind: "service", id: "f", displayName: "F", depth: 0, teamId: null, outDegree: 0, inDegree: 0 },
    { kind: "service", id: "a", displayName: "A", depth: 1, teamId: null, outDegree: 0, inDegree: 0 },
    { kind: "service", id: "b", displayName: "B", depth: 2, teamId: null, outDegree: 0, inDegree: 0 },
  ],
  edges: [],
  derivedEdges: [],
  truncated: false,
} as unknown as GraphResponse;

const graph: ExplorerGraph = {
  nodes: [
    { id: "service:f", kind: "service", entityId: "f", displayName: "F", outDegree: 0, inDegree: 0 },
    { id: "service:a", kind: "service", entityId: "a", displayName: "A", outDegree: 0, inDegree: 0 },
    { id: "service:x", kind: "service", entityId: "x", displayName: "X", outDegree: 0, inDegree: 0 },
  ],
  edges: [
    { id: "e1", source: "service:a", target: "service:f", label: "depends on" },
    { id: "e2", source: "service:x", target: "service:f", label: "depends on" },
  ],
  truncated: false,
};

describe("impactModel", () => {
  it("buildTierMap maps nodeId → depth", () => {
    const m = buildTierMap(impact);
    expect(m.get("service:f")).toBe(0);
    expect(m.get("service:a")).toBe(1);
    expect(m.get("service:b")).toBe(2);
  });

  it("impactDim dims everything not in the impacted set; edge dims iff an endpoint dims", () => {
    const impacted = new Set(["service:f", "service:a"]);
    const { dimmedNodeIds, dimmedEdgeIds } = impactDim(graph, impacted);
    expect(dimmedNodeIds.has("service:x")).toBe(true);
    expect(dimmedNodeIds.has("service:f")).toBe(false);
    expect(dimmedNodeIds.has("service:a")).toBe(false);
    expect(dimmedEdgeIds.has("e2")).toBe(true);  // x dims → e2 dims
    expect(dimmedEdgeIds.has("e1")).toBe(false); // a,f lit → e1 lit
  });

  it("tierCounts groups by tier, excludes focus (tier 0), ascending", () => {
    const m = new Map([["service:f", 0], ["service:a", 1], ["service:c", 1], ["service:b", 2]]);
    expect(tierCounts(m)).toEqual([{ tier: 1, count: 2 }, { tier: 2, count: 1 }]);
  });

  it("impactTotal excludes focus", () => {
    const m = new Map([["service:f", 0], ["service:a", 1], ["service:b", 2]]);
    expect(impactTotal(m)).toBe(2);
  });
});
