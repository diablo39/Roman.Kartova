import type { GraphResponse } from "@/features/catalog/api/graph";
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";

export type ExplorerNode = {
  id: string;
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  depth?: number;
};
export type ExplorerEdge = { id: string; source: string; target: string; label: string };
export type ExplorerGraph = { nodes: ExplorerNode[]; edges: ExplorerEdge[]; truncated: boolean };

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

export function mergeGraphs(results: GraphResponse[]): ExplorerGraph {
  const nodes = new Map<string, ExplorerNode>();
  const edges = new Map<string, ExplorerEdge>();
  let truncated = false;

  for (const r of results) {
    truncated = truncated || r.truncated;
    for (const n of r.nodes) {
      const id = nodeId(n.kind, n.id);
      if (!nodes.has(id)) {
        nodes.set(id, {
          id,
          kind: n.kind as RelationshipKind,
          entityId: n.id,
          displayName: n.displayName,
          depth: Number(n.depth),
        });
      }
    }
    for (const e of r.edges) {
      if (!edges.has(e.id)) {
        edges.set(e.id, {
          id: e.id,
          source: nodeId(e.source.kind, e.source.id),
          target: nodeId(e.target.kind, e.target.id),
          label: relationshipTypeLabel[e.type as CreatableRelationshipType] ?? e.type,
        });
      }
    }
  }
  return { nodes: [...nodes.values()], edges: [...edges.values()], truncated };
}
