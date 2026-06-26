// web/src/features/catalog/relationships/__tests__/graphLayout.test.ts
import { describe, it, expect } from "vitest";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

const graph: ExplorerGraph = {
  nodes: [
    { id: "service:f", kind: "service", entityId: "f", displayName: "Focus", depth: 0 },
    { id: "service:a", kind: "service", entityId: "a", displayName: "A", depth: 1 },
  ],
  edges: [{ id: "e1", source: "service:f", target: "service:a", label: "Depends on" }],
  truncated: false,
};

describe("layoutGraph", () => {
  it("assigns a position to every node and marks the focus node", () => {
    const { nodes, edges } = layoutGraph(graph, "service:f");
    expect(nodes).toHaveLength(2);
    expect(nodes.every((n) => Number.isFinite(n.position.x) && Number.isFinite(n.position.y))).toBe(true);
    expect(nodes.find((n) => n.id === "service:f")!.data.side).toBe("focused");
    expect(nodes.find((n) => n.id === "service:a")!.data.side).toBe("dependency");
    expect(edges).toHaveLength(1);
  });

  it("sets a detail href on non-focus nodes only", () => {
    const { nodes } = layoutGraph(graph, "service:f");
    expect(nodes.find((n) => n.id === "service:a")!.data.detailHref).toBe("/catalog/services/a");
    expect(nodes.find((n) => n.id === "service:f")!.data.detailHref).toBeUndefined();
  });
});
