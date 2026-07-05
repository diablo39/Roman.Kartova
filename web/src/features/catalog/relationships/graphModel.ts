import type { RelationshipResponse } from "@/features/catalog/api/relationships";
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphSide = "focused" | "dependency" | "dependent";

export type GraphNodeData = {
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  side: GraphSide;
  selected?: boolean; // explorer: the currently-selected node (sidebar open on it)
  dimmed?: boolean; // explorer: faded because it doesn't match the active filters (focus never dims)
};

export type GraphNode = {
  id: string;
  type: "entity";
  position: { x: number; y: number };
  data: GraphNodeData;
};

export type GraphEdge = { id: string; source: string; target: string; label: string };

export type GraphModel = { nodes: GraphNode[]; edges: GraphEdge[] };

export type FocusedEntity = { kind: RelationshipKind; id: string; displayName: string };

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

// Layout: dependents column (left) → focused (centre) → dependencies (right).
const COL_X: Record<GraphSide, number> = { dependent: 0, focused: 320, dependency: 640 };
const ROW_GAP = 90;

export function toGraphModel(focused: FocusedEntity, relationships: RelationshipResponse[]): GraphModel {
  const focusedId = nodeId(focused.kind, focused.id);
  const neighbours = new Map<string, GraphNodeData>();
  const edges: GraphEdge[] = [];

  for (const r of relationships) {
    const focusedIsSource = r.source.kind === focused.kind && r.source.id === focused.id;
    const focusedIsTarget = r.target.kind === focused.kind && r.target.id === focused.id;
    if (!focusedIsSource && !focusedIsTarget) continue;

    const other = focusedIsSource ? r.target : r.source;
    // FU-A: the graph doesn't render `api` (or any non-app/service) nodes yet — skip such
    // neighbours entirely so a backend-created API edge doesn't silently mis-route on click.
    if (other.kind !== "application" && other.kind !== "service") continue;
    const otherId = nodeId(other.kind, other.id);
    const side: GraphSide = focusedIsSource ? "dependency" : "dependent";

    const existing = neighbours.get(otherId);
    if (!existing) {
      neighbours.set(otherId, {
        kind: other.kind as RelationshipKind,
        entityId: other.id,
        displayName: other.displayName,
        side,
      });
    } else if (existing.side === "dependent" && side === "dependency") {
      existing.side = "dependency"; // a node that is both → prefer the dependency side
    }

    edges.push({
      id: r.id,
      source: nodeId(r.source.kind, r.source.id),
      target: nodeId(r.target.kind, r.target.id),
      label: relationshipTypeLabel[r.type as CreatableRelationshipType] ?? r.type,
    });
  }

  const nodes: GraphNode[] = [
    {
      id: focusedId,
      type: "entity",
      position: { x: COL_X.focused, y: 0 },
      data: { kind: focused.kind, entityId: focused.id, displayName: focused.displayName, side: "focused" },
    },
  ];

  let depRow = 0;
  let dentRow = 0;
  for (const [id, data] of neighbours) {
    const row = data.side === "dependency" ? depRow++ : dentRow++;
    nodes.push({ id, type: "entity", position: { x: COL_X[data.side], y: row * ROW_GAP }, data });
  }

  return { nodes, edges };
}

export const ENTITY_KIND_LABEL: Record<string, string> = { application: "Application", service: "Service" };

export function parseEntityRef(token: string | null | undefined): { kind: RelationshipKind; id: string } | null {
  if (!token) return null;
  const [kind, id] = token.split(":");
  if ((kind === "application" || kind === "service") && id) return { kind, id };
  return null;
}

export function entityDetailPath(kind: RelationshipKind, id: string): string {
  return `/catalog/${kind === "application" ? "applications" : "services"}/${id}`;
}
