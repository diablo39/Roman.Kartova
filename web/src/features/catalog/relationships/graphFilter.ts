import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphFilters = { kinds: RelationshipKind[]; teamIds: string[] };

/**
 * Pure filter pass over the in-memory merged graph. Returns the ids to dim;
 * never mutates the graph and never changes layout. The focus node is always
 * exempt. An edge dims iff either endpoint is dimmed. (S-05, spec §4.2.)
 */
export function applyGraphFilters(
  graph: ExplorerGraph,
  filters: GraphFilters,
  focusId: string,
): { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> } {
  // Empty facets match everything (the OR-with-`length === 0` short-circuits), so
  // with no active filter nothing is added — no separate "active" guard needed.
  const dimmedNodeIds = new Set<string>();
  for (const n of graph.nodes) {
    if (n.id === focusId) continue; // focus never dims
    const kindOk = filters.kinds.length === 0 || filters.kinds.includes(n.kind);
    const teamOk =
      filters.teamIds.length === 0 || (n.teamId != null && filters.teamIds.includes(n.teamId));
    if (!(kindOk && teamOk)) dimmedNodeIds.add(n.id);
  }

  const dimmedEdgeIds = new Set<string>();
  for (const e of graph.edges) {
    if (dimmedNodeIds.has(e.source) || dimmedNodeIds.has(e.target)) dimmedEdgeIds.add(e.id);
  }

  return { dimmedNodeIds, dimmedEdgeIds };
}
