import { describe, it, expect } from "vitest";
import {
  toGraphModel,
  parseEntityRef,
  entityDetailPath,
  derivedViaLabel,
  ENTITY_KIND_LABEL,
  type FocusedEntity,
} from "@/features/catalog/relationships/graphModel";
import type { RelationshipResponse } from "@/features/catalog/api/relationships";

const focused: FocusedEntity = { kind: "service", id: "s1", displayName: "Me" };
const focusedNodeId = "service:s1";

function rel(over: Partial<RelationshipResponse>): RelationshipResponse {
  return {
    id: "r",
    type: "dependsOn",
    origin: "manual",
    source: { kind: "service", id: "s1", displayName: "Me" },
    target: { kind: "service", id: "s2", displayName: "AuthService" },
    createdByUserId: "u1",
    createdAt: "2026-06-25T00:00:00Z",
    ...over,
  } as RelationshipResponse;
}

describe("toGraphModel", () => {
  it("returns only the focused node when there are no relationships", () => {
    const m = toGraphModel(focused, []);
    expect(m.nodes).toHaveLength(1);
    expect(m.nodes[0]!.id).toBe(focusedNodeId);
    expect(m.nodes[0]!.data.side).toBe("focused");
    expect(m.edges).toHaveLength(0);
  });

  it("places an outgoing edge's other endpoint on the dependency side", () => {
    const m = toGraphModel(focused, [rel({ id: "r1" })]); // focused is source
    const other = m.nodes.find((n) => n.id === "service:s2")!;
    expect(other.data.side).toBe("dependency");
    expect(m.edges).toEqual([{ id: "r1", source: "service:s1", target: "service:s2", label: "Depends on" }]);
  });

  it("places an incoming edge's other endpoint on the dependent side", () => {
    const m = toGraphModel(focused, [
      rel({ id: "r2", source: { kind: "application", id: "a1", displayName: "Checkout" }, target: { kind: "service", id: "s1", displayName: "Me" } }),
    ]);
    const other = m.nodes.find((n) => n.id === "application:a1")!;
    expect(other.data.side).toBe("dependent");
    expect(m.edges).toEqual([{ id: "r2", source: "application:a1", target: "service:s1", label: "Depends on" }]);
  });

  it("dedupes a neighbour seen in both directions to one node (dependency side) with both edges", () => {
    const m = toGraphModel(focused, [
      rel({ id: "out", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" } }),
      rel({ id: "in", source: { kind: "service", id: "s2", displayName: "AuthService" }, target: { kind: "service", id: "s1", displayName: "Me" } }),
    ]);
    expect(m.nodes.filter((n) => n.id === "service:s2")).toHaveLength(1);
    expect(m.nodes.find((n) => n.id === "service:s2")!.data.side).toBe("dependency");
    expect(m.edges).toHaveLength(2);
  });

  it("ignores a relationship that does not reference the focused entity", () => {
    const m = toGraphModel(focused, [
      rel({ id: "x", source: { kind: "service", id: "zzz", displayName: "Other" }, target: { kind: "service", id: "yyy", displayName: "Another" } }),
    ]);
    expect(m.nodes).toHaveLength(1); // focused only
    expect(m.edges).toHaveLength(0);
  });

  it("includes a providesApiFor edge's other endpoint as an api-kind node", () => {
    const m = toGraphModel(focused, [
      rel({
        id: "api1",
        type: "providesApiFor",
        source: { kind: "service", id: "s1", displayName: "Me" },
        target: { kind: "api", id: "api-1", displayName: "Orders API" } as RelationshipResponse["target"],
      }),
    ]);
    const other = m.nodes.find((n) => n.id === "api:api-1")!;
    expect(other.data.kind).toBe("api");
    expect(other.data.side).toBe("dependency");
    expect(m.edges).toEqual([{ id: "api1", source: "service:s1", target: "api:api-1", label: "Provides API for" }]);
  });

  it("labels and routes api kind", () => {
    expect(ENTITY_KIND_LABEL.api).toBe("API");
    expect(entityDetailPath("api", "x1")).toBe("/catalog/apis/x1");
  });

  it("parses an api entity ref", () => {
    expect(parseEntityRef("api:abc")).toEqual({ kind: "api", id: "abc" });
    expect(parseEntityRef("broker:abc")).toBeNull();
  });

  it("rejects a token missing the id", () => {
    expect(parseEntityRef("api")).toBeNull();
    expect(parseEntityRef("api:")).toBeNull();
  });
});

describe("derivedViaLabel", () => {
  it("formats a single distinct api name", () => {
    expect(derivedViaLabel(["Orders API"])).toBe("via Orders API");
  });

  it("collapses two paths through the same api to one name (no +1)", () => {
    expect(derivedViaLabel(["Orders API", "Orders API"])).toBe("via Orders API");
  });

  it("appends a +N suffix for multiple distinct api names", () => {
    expect(derivedViaLabel(["Orders API", "Billing API"])).toBe("via Orders API +1");
  });

  it("dedupes before counting distinct names for the +N suffix", () => {
    expect(derivedViaLabel(["Orders API", "Orders API", "Billing API"])).toBe("via Orders API +1");
  });
});

describe("toGraphModel derived edges", () => {
  it("adds dashed edge + node for a derived dependency (focused → other)", () => {
    const m = toGraphModel(focused, [], {
      dependencies: [{ serviceId: "t9", displayName: "Provider", label: "via Orders API" }],
      dependents: [],
    });
    const other = m.nodes.find((n) => n.id === "service:t9")!;
    expect(other.data.side).toBe("dependency");
    const edge = m.edges.find((e) => e.derived)!;
    expect(edge.id).toBe("service:s1->service:t9:derived");
    expect(edge.source).toBe("service:s1");
    expect(edge.target).toBe("service:t9");
    expect(edge.label).toBe("via Orders API");
  });

  it("adds dashed edge + node for a derived dependent (other → focused)", () => {
    const m = toGraphModel(focused, [], {
      dependencies: [],
      dependents: [{ serviceId: "s9", displayName: "Consumer", label: "via Payments API" }],
    });
    const other = m.nodes.find((n) => n.id === "service:s9")!;
    expect(other.data.side).toBe("dependent");
    const edge = m.edges.find((e) => e.derived)!;
    expect(edge.id).toBe("service:s9->service:s1:derived");
    expect(edge.source).toBe("service:s9");
    expect(edge.target).toBe("service:s1");
  });

  it("does not add a self-edge when a derived neighbour is the focused entity itself", () => {
    const m = toGraphModel(focused, [], {
      dependencies: [{ serviceId: "s1", displayName: "Me", label: "via Orders API" }],
      dependents: [],
    });
    expect(m.edges.some((e) => e.derived)).toBe(false);
    expect(m.nodes).toHaveLength(1);
  });

  it("dedupes a derived neighbour already present as a persisted neighbour, still adding the derived edge", () => {
    const m = toGraphModel(
      focused,
      [rel({ id: "out", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" } })],
      {
        dependencies: [{ serviceId: "s2", displayName: "AuthService", label: "via Orders API" }],
        dependents: [],
      },
    );
    expect(m.nodes.filter((n) => n.id === "service:s2")).toHaveLength(1);
    expect(m.edges.filter((e) => e.derived)).toHaveLength(1);
    expect(m.edges).toHaveLength(2);
  });
});
