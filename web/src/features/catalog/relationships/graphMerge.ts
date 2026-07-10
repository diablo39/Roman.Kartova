import type { GraphResponse } from "@/features/catalog/api/graph";
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";
import { derivedViaLabel } from "@/features/catalog/relationships/graphModel";

export type ExplorerNode = {
  id: string;
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  depth?: number;
  teamId?: string;
  outDegree: number;
  inDegree: number;
};
export type ExplorerEdge = {
  id: string;
  source: string;
  target: string;
  label: string;
  derived?: boolean;
  provenance?: { apiName: string; viaAppName?: string | null }[];
};
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
          kind: n.kind,
          entityId: n.id,
          displayName: n.displayName,
          depth: Number(n.depth),
          teamId: n.teamId ?? undefined,
          outDegree: Number(n.outDegree ?? 0),
          inDegree: Number(n.inDegree ?? 0),
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
    for (const d of r.derivedEdges ?? []) {
      const source = nodeId(d.source.kind, d.source.id);
      const target = nodeId(d.target.kind, d.target.id);
      const id = `${source}->${target}:derived`;
      if (edges.has(id)) continue;
      const label = `depends on · ${derivedViaLabel(d.paths.map((p) => p.apiName))}`;
      edges.set(id, {
        id,
        source,
        target,
        label,
        derived: true,
        // Full per-path provenance is carried for B2's DerivedDependenciesSection (per-API/app expander);
        // B1's explorer surfaces the summary via the compact label above.
        provenance: d.paths.map((p) => ({ apiName: p.apiName, viaAppName: p.viaApplicationDisplayName })),
      });
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

export function loadedDegrees(graph: ExplorerGraph): Map<string, { out: number; in: number }> {
  const m = new Map<string, { out: number; in: number }>();
  const bump = (id: string, dir: "out" | "in") => {
    const e = m.get(id) ?? { out: 0, in: 0 };
    e[dir] += 1;
    m.set(id, e);
  };
  for (const e of graph.edges) {
    if (e.derived) continue; // degree from backend counts explicit edges only
    bump(e.source, "out");
    bump(e.target, "in");
  }
  return m;
}
