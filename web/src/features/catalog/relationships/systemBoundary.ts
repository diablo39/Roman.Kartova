import type { Node } from "@xyflow/react";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { NODE_W, NODE_H } from "@/features/catalog/relationships/graphLayout";

export const BOUNDARY_PADDING = 24;

/** The wire relationship type that marks System membership (component -> System). */
export const PART_OF_TYPE = "partOf" as const;

/**
 * Members of the focused System, read off the edge direction rather than a label or a depth
 * guess: PartOf runs component -> System (ADR-0111), so a member is the SOURCE of a partOf
 * edge whose TARGET is the focus. At depth 2 an external neighbour can carry its own partOf
 * edge to a different System; that must not make it a member here.
 */
export function systemMemberIds(graph: ExplorerGraph, focusId: string): Set<string> {
  const ids = new Set<string>();
  for (const e of graph.edges) {
    if (e.type === PART_OF_TYPE && e.target === focusId) ids.add(e.source);
  }
  return ids;
}

/**
 * Bounding box over the focus node and its members, in flow coordinates, padded. Returns null
 * when the System has no members — there is nothing to enclose and the caller skips the band.
 */
export function systemBoundaryBox(
  nodes: Node<GraphNodeData>[],
  memberIds: Set<string>,
  focusId: string,
): { x: number; y: number; width: number; height: number } | null {
  if (memberIds.size === 0) return null;
  const inside = nodes.filter((n) => n.id === focusId || memberIds.has(n.id));
  if (inside.length === 0) return null;

  const minX = Math.min(...inside.map((n) => n.position.x));
  const minY = Math.min(...inside.map((n) => n.position.y));
  const maxX = Math.max(...inside.map((n) => n.position.x + NODE_W));
  const maxY = Math.max(...inside.map((n) => n.position.y + NODE_H));

  return {
    x: minX - BOUNDARY_PADDING,
    y: minY - BOUNDARY_PADDING,
    width: maxX - minX + BOUNDARY_PADDING * 2,
    height: maxY - minY + BOUNDARY_PADDING * 2,
  };
}
