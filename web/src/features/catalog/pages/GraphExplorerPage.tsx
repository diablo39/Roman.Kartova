import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ReactFlow, Background, Controls, MiniMap, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useGraph } from "@/features/catalog/api/graph";
import { mergeGraphs, bfsDepth } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { useExplorerState } from "@/features/catalog/relationships/useExplorerState";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { parseEntityRef } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };
const SOFT_CAP = 150;

export function GraphExplorerPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const focus = parseEntityRef(params.get("focus"));
  const focusId = focus ? `${focus.kind}:${focus.id}` : "";
  const { expand, selected, isExpanded, toggleExpand, select, reset } = useExplorerState(focusId);

  const safeFocus = focus ?? { kind: "application" as RelationshipKind, id: "" };
  const { results, isLoading, isError, expandError, refetch } = useGraph({ focus: safeFocus, expand });

  const merged = useMemo(
    () => (focusId ? mergeGraphs(results) : { nodes: [], edges: [], truncated: false }),
    [results, focusId],
  );
  const atCap = merged.nodes.length >= SOFT_CAP;
  const { nodes, edges } = useMemo(
    () => (focusId ? layoutGraph(merged, focusId, selected) : { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] }),
    [merged, focusId, selected],
  );

  // Only show the sidebar for a node actually present in the current graph.
  const selectedRef = useMemo(
    () => (selected && merged.nodes.some((n) => n.id === selected) ? parseEntityRef(selected) : null),
    [selected, merged],
  );
  const depthFromFocus = useMemo(
    () => (selected ? bfsDepth(merged, focusId, selected) : null),
    [merged, focusId, selected],
  );

  if (!focus) {
    return <div className="p-8 text-sm text-tertiary">Pick an entity to explore its dependency graph.</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] flex-col gap-2 p-4">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Dependency graph</h1>
        <button type="button" onClick={reset} className="text-sm text-brand-primary underline">Reset to focus</button>
      </div>
      {isLoading ? (
        <Skeleton className="h-full w-full" />
      ) : isError ? (
        <div className="flex items-center gap-3">
          <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
          <button type="button" className="text-sm text-brand-primary underline" onClick={() => refetch()}>Try again</button>
        </div>
      ) : (
        <>
          {atCap && (
            <p className="text-xs text-warning-primary">Large graph (≥{SOFT_CAP} nodes) — collapse a branch or Reset to keep it readable.</p>
          )}
          {expandError && (
            <p className="text-xs text-warning-primary">Some expansions failed to load — try expanding again.</p>
          )}
          <div className="flex min-h-0 flex-1 gap-2">
            <div className="min-h-0 flex-1 overflow-hidden rounded-lg ring-1 ring-secondary">
              <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={NODE_TYPES}
                fitView
                nodesDraggable={false}
                nodesConnectable={false}
                elementsSelectable={false}
                proOptions={{ hideAttribution: true }}
                onNodeClick={(_, node) => select(node.id)}
              >
                <Background />
                <Controls showInteractive={false} />
                <MiniMap pannable zoomable />
              </ReactFlow>
            </div>
            {selectedRef && (
              <GraphExplorerSidebar
                selected={selectedRef}
                depthFromFocus={depthFromFocus}
                isExpanded={isExpanded}
                atCap={atCap}
                onToggleExpand={toggleExpand}
                onSetFocus={() => navigate(`/graph?focus=${selectedRef.kind}:${selectedRef.id}`)}
                onClose={() => select(null)}
              />
            )}
          </div>
        </>
      )}
    </div>
  );
}
