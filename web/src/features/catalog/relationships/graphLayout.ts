// web/src/features/catalog/relationships/graphLayout.ts
import dagre from "@dagrejs/dagre";
import type { Node, Edge } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_W = 180;
const NODE_H = 56;

const detailHref = (kind: RelationshipKind, id: string) =>
  `/catalog/${kind === "application" ? "applications" : "services"}/${id}`;

export function layoutGraph(graph: ExplorerGraph, focusId: string): { nodes: Node<GraphNodeData>[]; edges: Edge[] } {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: "LR", nodesep: 40, ranksep: 120 });
  g.setDefaultEdgeLabel(() => ({}));
  for (const n of graph.nodes) g.setNode(n.id, { width: NODE_W, height: NODE_H });
  for (const e of graph.edges) g.setEdge(e.source, e.target);
  dagre.layout(g);

  const nodes: Node<GraphNodeData>[] = graph.nodes.map((n) => {
    const pos = g.node(n.id);
    const isFocus = n.id === focusId;
    return {
      id: n.id,
      type: "entity",
      position: { x: pos.x - NODE_W / 2, y: pos.y - NODE_H / 2 },
      data: {
        kind: n.kind,
        entityId: n.entityId,
        displayName: n.displayName,
        side: isFocus ? "focused" : "dependency",
        detailHref: isFocus ? undefined : detailHref(n.kind, n.entityId),
      },
    };
  });

  const edges: Edge[] = graph.edges.map((e) => ({
    id: e.id,
    source: e.source,
    target: e.target,
    label: e.label,
  }));

  return { nodes, edges };
}
