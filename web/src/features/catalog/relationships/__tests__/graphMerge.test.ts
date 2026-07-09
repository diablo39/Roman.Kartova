import { describe, it, expect } from "vitest";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const node = (id: string, displayName: string, depth: number) =>
  ({ kind: "service", id, displayName, depth, teamId: null }) as GraphResponse["nodes"][number];
const edge = (id: string, s: string, t: string) =>
  ({ id, source: { kind: "service", id: s }, target: { kind: "service", id: t }, type: "dependsOn", origin: "manual" }) as GraphResponse["edges"][number];

describe("mergeGraphs", () => {
  it("maps one response to nodes keyed by kind:id with labelled edges", () => {
    const r: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false, derivedEdges: [] };
    const g = mergeGraphs([r]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:f"]);
    expect(g.edges).toEqual([{ id: "e1", source: "service:f", target: "service:a", label: "Depends on" }]);
    expect(g.truncated).toBe(false);
  });

  it("dedupes a node and an edge that appear in two responses", () => {
    const r1: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false, derivedEdges: [] };
    const r2: GraphResponse = { nodes: [node("a", "A", 0), node("b", "B", 1)], edges: [edge("e1", "f", "a"), edge("e2", "a", "b")], truncated: false, derivedEdges: [] };
    const g = mergeGraphs([r1, r2]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:b", "service:f"]);
    expect(g.edges.map((e) => e.id).sort()).toEqual(["e1", "e2"]);
  });

  it("ORs the truncated flag across responses", () => {
    expect(mergeGraphs([{ nodes: [], edges: [], truncated: false, derivedEdges: [] }, { nodes: [], edges: [], truncated: true, derivedEdges: [] }]).truncated).toBe(true);
  });

  it("unions an outgoing result and an incoming result for the same node", () => {
    const out: GraphResponse = { nodes: [node("a","A",0), node("b","B",1)], edges: [edge("e1","a","b")], truncated: false, derivedEdges: [] };
    const inc: GraphResponse = { nodes: [node("a","A",0), node("c","C",1)], edges: [edge("e2","c","a")], truncated: false, derivedEdges: [] };
    const g = mergeGraphs([out, inc]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a","service:b","service:c"]);
    expect(g.edges.map((e) => e.id).sort()).toEqual(["e1","e2"]);
  });

  it("includes api nodes and edges", () => {
    const merged = mergeGraphs([
      {
        nodes: [
          { kind: "service", id: "s1", displayName: "Me", depth: 0, teamId: "t1" },
          { kind: "api", id: "api-1", displayName: "Orders API", depth: 1, teamId: "t1" },
        ],
        edges: [
          { id: "e1", source: { kind: "service", id: "s1" }, target: { kind: "api", id: "api-1" }, type: "providesApiFor", origin: "manual" },
        ],
        truncated: false,
      } as never,
    ]);
    expect(merged.nodes.find((n) => n.id === "api:api-1")?.kind).toBe("api");
    expect(merged.edges).toEqual([{ id: "e1", source: "service:s1", target: "api:api-1", label: "Provides API for" }]);
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

describe("teamId threading", () => {
  it("threads teamId from the response onto the merged node (null → undefined)", () => {
    const merged = mergeGraphs([
      {
        truncated: false,
        nodes: [
          { kind: "application", id: "a1", displayName: "App 1", depth: 0, teamId: "team-1" },
          { kind: "service", id: "s1", displayName: "Svc 1", depth: 1, teamId: null },
        ],
        edges: [],
      } as unknown as GraphResponse,
    ]);
    expect(merged.nodes.find((n) => n.id === "application:a1")?.teamId).toBe("team-1");
    expect(merged.nodes.find((n) => n.id === "service:s1")?.teamId).toBeUndefined();
  });
});

const S = "11111111-1111-1111-1111-111111111111";
const T = "22222222-2222-2222-2222-222222222222";
const API = "66666666-6666-6666-6666-666666666666";
const API2 = "77777777-7777-7777-7777-777777777777";
const APP = "44444444-4444-4444-4444-444444444444";

describe("mergeGraphs — derived edges", () => {
  it("folds a derivedEdges entry into a dashed, provenance-labelled ExplorerEdge", () => {
    const r = {
      nodes: [node(S, "S", 0), node(T, "T", 1)],
      edges: [],
      truncated: false,
      derivedEdges: [
        {
          source: { kind: "service", id: S },
          target: { kind: "service", id: T },
          paths: [{ apiId: API, apiName: "Orders API", viaApplicationId: APP, viaApplicationDisplayName: "App 1" }],
        },
      ],
    } as unknown as GraphResponse;
    const g = mergeGraphs([r]);
    const edge = g.edges.find((e) => e.derived);
    expect(edge).toBeDefined();
    expect(edge!.id).toBe(`service:${S}->service:${T}:derived`);
    expect(edge!.source).toBe(`service:${S}`);
    expect(edge!.target).toBe(`service:${T}`);
    expect(edge!.label).toBe("depends on · via Orders API");
    expect(edge!.provenance).toEqual([{ apiName: "Orders API", viaAppName: "App 1" }]);
  });

  it("compacts the label when there are multiple provenance paths", () => {
    const r = {
      nodes: [node(S, "S", 0), node(T, "T", 1)],
      edges: [],
      truncated: false,
      derivedEdges: [
        {
          source: { kind: "service", id: S },
          target: { kind: "service", id: T },
          paths: [
            { apiId: API, apiName: "Orders API", viaApplicationId: APP, viaApplicationDisplayName: "App 1" },
            { apiId: API2, apiName: "Billing API", viaApplicationId: null, viaApplicationDisplayName: null },
          ],
        },
      ],
    } as unknown as GraphResponse;
    const g = mergeGraphs([r]);
    const edge = g.edges.find((e) => e.derived);
    expect(edge!.label).toBe("depends on · via Orders API +1");
  });

  it("skips a derived edge when an explicit edge already exists for that id", () => {
    const r = { nodes: [], edges: [], truncated: false, derivedEdges: undefined } as unknown as GraphResponse;
    const g = mergeGraphs([r]);
    expect(g.edges.some((e) => e.derived)).toBe(false);
  });
});
