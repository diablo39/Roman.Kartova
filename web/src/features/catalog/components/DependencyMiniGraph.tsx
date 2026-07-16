import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ReactFlow, Background, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { useDerivedDependencies, type DerivedDependencyItem } from "@/features/catalog/api/derivedDependencies";
import {
  toGraphModel,
  derivedViaLabel,
  entityDetailPath,
  type FocusedEntity,
  type GraphNodeData,
} from "@/features/catalog/relationships/graphModel";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphActionsProvider } from "@/features/catalog/relationships/GraphActionsContext";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };
const GRAPH_LIMIT = 50;

function toNeighbour(d: DerivedDependencyItem) {
  return {
    serviceId: d.serviceId,
    displayName: d.displayName,
    label: derivedViaLabel(d.paths.map((p) => p.apiName)),
  };
}

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  displayName: string;
}

export function DependencyMiniGraph({ entityKind, entityId, displayName }: Props) {
  const navigate = useNavigate();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const list = useRelationshipsList({ entityKind, entityId, direction: "all", limit: GRAPH_LIMIT });
  const derivedQuery = useDerivedDependencies(entityId, { enabled: entityKind === "service" });

  // Match the standalone /graph explorer's interaction: a node click SELECTS (highlights)
  // rather than navigating; navigation is an explicit "Open page ↗" in the node's ⋯ menu.
  const actions = useMemo(
    () => ({
      toggleExpand: () => {}, // mini-graph is a fixed 1-hop preview — not expandable
      setFocus: (kind: RelationshipKind, id: string) => navigate(`/graph?focus=${kind}:${id}`),
      openPage: (kind: RelationshipKind, id: string) => navigate(entityDetailPath(kind, id)),
      atCap: false,
    }),
    [navigate],
  );

  const model = useMemo(() => {
    const focused: FocusedEntity = { kind: entityKind, id: entityId, displayName };
    const derived = derivedQuery.data
      ? {
          dependencies: derivedQuery.data.dependencies.map(toNeighbour),
          dependents: derivedQuery.data.dependents.map(toNeighbour),
        }
      : undefined;
    return toGraphModel(focused, list.items ?? [], derived);
  }, [list.items, entityKind, entityId, displayName, derivedQuery.data]);

  const nodes = useMemo(
    () => model.nodes.map((n) => ({ ...n, data: { ...n.data, selected: n.id === selectedId } })) as Node[],
    [model.nodes, selectedId],
  );

  return (
    <section className="space-y-2" aria-label="Dependency graph">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-primary">Dependency graph</h3>
        <Link to={`/graph?focus=${entityKind}:${entityId}`} className="text-xs text-brand-secondary underline">
          Open full graph ↗
        </Link>
      </div>
      {list.isLoading ? (
        <Skeleton className="h-80 w-full" />
      ) : list.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
      ) : model.edges.length === 0 ? (
        <p className="text-sm italic text-tertiary">No dependencies yet.</p>
      ) : (
        <>
          <div className="h-80 w-full overflow-hidden rounded-lg ring-1 ring-secondary">
            <GraphActionsProvider value={actions}>
              <ReactFlow
                nodes={nodes}
                edges={model.edges.map((e) => ({
                  ...e,
                  ...(e.derived ? { style: { strokeDasharray: "6 4", stroke: "var(--color-fg-quaternary, #98A2B3)" } } : {}),
                })) as Edge[]}
                nodeTypes={NODE_TYPES}
                fitView
                nodesDraggable={false}
                nodesConnectable={false}
                elementsSelectable={false}
                proOptions={{ hideAttribution: true }}
                onNodeClick={(_, node) => {
                  const data = node.data as GraphNodeData;
                  if (data.side === "focused") return;
                  setSelectedId(node.id);
                }}
              >
                <Background />
              </ReactFlow>
            </GraphActionsProvider>
          </div>
          <p className="text-xs text-tertiary">
            <span className="mr-3">— explicit</span>
            <span className="font-mono">- - derived</span>
          </p>
          {list.hasNext && (
            <p className="text-xs text-tertiary">
              Showing the first {GRAPH_LIMIT} relationships — see the tables below for the full list.
            </p>
          )}
          {entityKind === "service" && derivedQuery.isError && (
            <p className="text-xs text-error-primary">Derived dependencies couldn&apos;t be loaded.</p>
          )}
        </>
      )}
    </section>
  );
}
