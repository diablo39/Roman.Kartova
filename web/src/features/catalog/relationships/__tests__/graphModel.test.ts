import { describe, it, expect } from "vitest";
import { toGraphModel, type FocusedEntity } from "@/features/catalog/relationships/graphModel";
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
});
