import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

/** nodeId → tier (hop distance) from the impact response. Focus is depth 0. */
export function buildTierMap(impact: GraphResponse): Map<string, number> {
  const m = new Map<string, number>();
  for (const n of impact.nodes) m.set(nodeId(n.kind, n.id), Number(n.depth));
  return m;
}

/** Dim every merged node NOT in the impacted set; an edge dims iff either endpoint dims.
 *  Mirrors applyGraphFilters' return shape so the page can union the two dim sets. */
export function impactDim(
  graph: ExplorerGraph,
  impactNodeIds: Set<string>,
): { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> } {
  const dimmedNodeIds = new Set<string>();
  for (const n of graph.nodes) if (!impactNodeIds.has(n.id)) dimmedNodeIds.add(n.id);
  const dimmedEdgeIds = new Set<string>();
  for (const e of graph.edges)
    if (dimmedNodeIds.has(e.source) || dimmedNodeIds.has(e.target)) dimmedEdgeIds.add(e.id);
  return { dimmedNodeIds, dimmedEdgeIds };
}

/** Per-tier counts for the banner, ascending, excluding tier 0 (focus). */
export function tierCounts(tierByNodeId: Map<string, number>): { tier: number; count: number }[] {
  const counts = new Map<number, number>();
  for (const t of tierByNodeId.values()) {
    if (t === 0) continue;
    counts.set(t, (counts.get(t) ?? 0) + 1);
  }
  return [...counts.entries()].sort((a, b) => a[0] - b[0]).map(([tier, count]) => ({ tier, count }));
}

/** Total downstream count (excludes focus). */
export function impactTotal(tierByNodeId: Map<string, number>): number {
  let n = 0;
  for (const t of tierByNodeId.values()) if (t !== 0) n++;
  return n;
}
