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
  teamId?: string;
};
export type ExplorerEdge = { id: string; source: string; target: string; label: string };
export type ExplorerGraph = { nodes: ExplorerNode[]; edges: ExplorerEdge[]; truncated: boolean };

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

// FU-A: the explorer graph doesn't render `api` (or any non-app/service) nodes yet — filter
// them out here so a backend-created API edge can't reach the explorer and mis-route on click.
const isRenderableKind = (kind: string): kind is RelationshipKind => kind === "application" || kind === "service";

export function mergeGraphs(results: GraphResponse[]): ExplorerGraph {
  const nodes = new Map<string, ExplorerNode>();
  const edges = new Map<string, ExplorerEdge>();
  let truncated = false;

  for (const r of results) {
    truncated = truncated || r.truncated;
    for (const n of r.nodes) {
      if (!isRenderableKind(n.kind)) continue;
      const id = nodeId(n.kind, n.id);
      if (!nodes.has(id)) {
        nodes.set(id, {
          id,
          kind: n.kind,
          entityId: n.id,
          displayName: n.displayName,
          depth: Number(n.depth),
          teamId: n.teamId ?? undefined,
        });
      }
    }
    for (const e of r.edges) {
      if (!isRenderableKind(e.source.kind) || !isRenderableKind(e.target.kind)) continue;
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

export function bfsDepth(graph: ExplorerGraph, fromId: string, toId: string): number | null {
  if (fromId === toId) return 0;
  const adj = new Map<string, string[]>();
  for (const e of graph.edges) {
    (adj.get(e.source) ?? adj.set(e.source, []).get(e.source)!).push(e.target);
    (adj.get(e.target) ?? adj.set(e.target, []).get(e.target)!).push(e.source);
  }
  const seen = new Set<string>([fromId]);
  let frontier = [fromId];
  let depth = 0;
  while (frontier.length) {
    depth++;
    const next: string[] = [];
    for (const id of frontier) {
      for (const nb of adj.get(id) ?? []) {
        if (nb === toId) return depth;
        if (!seen.has(nb)) { seen.add(nb); next.push(nb); }
      }
    }
    frontier = next;
  }
  return null;
}
