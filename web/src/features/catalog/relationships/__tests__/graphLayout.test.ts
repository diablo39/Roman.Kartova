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
    const { nodes, edges } = layoutGraph(graph, "service:f", null);
    expect(nodes).toHaveLength(2);
    expect(nodes.every((n) => Number.isFinite(n.position.x) && Number.isFinite(n.position.y))).toBe(true);
    expect(nodes.find((n) => n.id === "service:f")!.data.side).toBe("focused");
    expect(nodes.find((n) => n.id === "service:a")!.data.side).toBe("dependency");
    expect(edges).toHaveLength(1);
  });

  it("marks the selected node", () => {
    const { nodes } = layoutGraph(graph, "service:f", "service:a");
    expect(nodes.find((n) => n.id === "service:a")!.data.selected).toBe(true);
    expect(nodes.find((n) => n.id === "service:f")!.data.selected).not.toBe(true);
  });

  it("threads dimmed flags onto node data and edge style", () => {
    const dimGraph = {
      truncated: false,
      nodes: [
        { id: "application:focus", kind: "application", entityId: "focus", displayName: "Focus" },
        { id: "service:s1", kind: "service", entityId: "s1", displayName: "Svc 1" },
      ],
      edges: [{ id: "e1", source: "application:focus", target: "service:s1", label: "depends on" }],
    } as const;
    const { nodes, edges } = layoutGraph(
      dimGraph as unknown as Parameters<typeof layoutGraph>[0],
      "application:focus",
      null,
      { nodeIds: new Set(["service:s1"]), edgeIds: new Set(["e1"]) },
    );
    expect(nodes.find((n) => n.id === "service:s1")?.data.dimmed).toBe(true);
    expect(nodes.find((n) => n.id === "application:focus")?.data.dimmed).toBe(false);
    expect(edges.find((e) => e.id === "e1")?.style?.opacity).toBe(0.2);
  });
});
