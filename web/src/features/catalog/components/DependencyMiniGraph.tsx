import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { ReactFlow, Background, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { toGraphModel, type FocusedEntity, type GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };
const GRAPH_LIMIT = 50;

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  displayName: string;
}

export function DependencyMiniGraph({ entityKind, entityId, displayName }: Props) {
  const navigate = useNavigate();
  const list = useRelationshipsList({ entityKind, entityId, direction: "all", limit: GRAPH_LIMIT });

  const model = useMemo(() => {
    const focused: FocusedEntity = { kind: entityKind, id: entityId, displayName };
    return toGraphModel(focused, list.items ?? []);
  }, [list.items, entityKind, entityId, displayName]);

  return (
    <section className="space-y-2" aria-label="Dependency graph">
      <h3 className="text-sm font-semibold text-primary">Dependency graph</h3>
      {list.isLoading ? (
        <Skeleton className="h-80 w-full" />
      ) : list.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
      ) : model.edges.length === 0 ? (
        <p className="text-sm italic text-tertiary">No dependencies yet.</p>
      ) : (
        <>
          <div className="h-80 w-full overflow-hidden rounded-lg ring-1 ring-secondary">
            <ReactFlow
              nodes={model.nodes as Node[]}
              edges={model.edges as Edge[]}
              nodeTypes={NODE_TYPES}
              fitView
              nodesDraggable={false}
              nodesConnectable={false}
              elementsSelectable={false}
              proOptions={{ hideAttribution: true }}
              onNodeClick={(_, node) => {
                const data = node.data as GraphNodeData;
                if (data.side === "focused") return;
                navigate(`/catalog/${data.kind === "application" ? "applications" : "services"}/${data.entityId}`);
              }}
            >
              <Background />
            </ReactFlow>
          </div>
          {list.hasNext && (
            <p className="text-xs text-tertiary">
              Showing the first {GRAPH_LIMIT} relationships — see the tables below for the full list.
            </p>
          )}
        </>
      )}
    </section>
  );
}
