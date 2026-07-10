import { describe, it, expect } from "vitest";
import { applyGraphFilters, type GraphFilters } from "@/features/catalog/relationships/graphFilter";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

const graph: ExplorerGraph = {
  truncated: false,
  nodes: [
    { id: "application:focus", kind: "application", entityId: "focus", displayName: "Focus", teamId: "t1", outDegree: 0, inDegree: 0 },
    { id: "application:a1", kind: "application", entityId: "a1", displayName: "App 1", teamId: "t1", outDegree: 0, inDegree: 0 },
    { id: "service:s1", kind: "service", entityId: "s1", displayName: "Svc 1", teamId: "t2", outDegree: 0, inDegree: 0 },
    { id: "service:s2", kind: "service", entityId: "s2", displayName: "Svc 2", teamId: undefined, outDegree: 0, inDegree: 0 },
  ],
  edges: [
    { id: "e-focus-a1", source: "application:focus", target: "application:a1", label: "depends on" },
    { id: "e-a1-s1", source: "application:a1", target: "service:s1", label: "depends on" },
  ],
};
const focusId = "application:focus";
const empty: GraphFilters = { kinds: [], teamIds: [] };

describe("applyGraphFilters", () => {
  it("dims nothing when no facet is active", () => {
    const { dimmedNodeIds, dimmedEdgeIds } = applyGraphFilters(graph, empty, focusId);
    expect(dimmedNodeIds.size).toBe(0);
    expect(dimmedEdgeIds.size).toBe(0);
  });

  it("kind filter dims the other kind but never the focus", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: [] }, focusId);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);
    expect(dimmedNodeIds.has("service:s2")).toBe(true);
    expect(dimmedNodeIds.has("application:a1")).toBe(false);
    expect(dimmedNodeIds.has("application:focus")).toBe(false); // focus exempt
  });

  it("team filter dims other teams and null-team nodes (focus exempt)", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: [], teamIds: ["t1"] }, focusId);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);  // t2
    expect(dimmedNodeIds.has("service:s2")).toBe(true);  // null team
    expect(dimmedNodeIds.has("application:a1")).toBe(false); // t1
    expect(dimmedNodeIds.has("application:focus")).toBe(false);
  });

  it("ANDs facets together", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: ["t2"] }, focusId);
    // only focus is exempt; a1 is application but t1 (fails team); s1 is t2 but service (fails kind)
    expect(dimmedNodeIds.has("application:a1")).toBe(true);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);
    expect(dimmedNodeIds.has("application:focus")).toBe(false);
  });

  it("dims an edge iff either endpoint is dimmed", () => {
    const { dimmedEdgeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: [] }, focusId);
    expect(dimmedEdgeIds.has("e-focus-a1")).toBe(false); // both endpoints applications
    expect(dimmedEdgeIds.has("e-a1-s1")).toBe(true);     // s1 dimmed
  });

  it("api kind filter dims non-api nodes but never the focus", () => {
    const graphWithApi: ExplorerGraph = {
      ...graph,
      nodes: [
        ...graph.nodes,
        { id: "api:api1", kind: "api", entityId: "api1", displayName: "Orders API", teamId: "t1", outDegree: 0, inDegree: 0 },
      ],
    };
    const { dimmedNodeIds } = applyGraphFilters(graphWithApi, { kinds: ["api"], teamIds: [] }, focusId);
    expect(dimmedNodeIds.has("api:api1")).toBe(false);
    expect(dimmedNodeIds.has("application:a1")).toBe(true);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);
    expect(dimmedNodeIds.has("application:focus")).toBe(false); // focus exempt
  });
});
