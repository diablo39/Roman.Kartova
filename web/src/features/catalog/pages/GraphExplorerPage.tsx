import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { ReactFlow, Background, Controls, MiniMap, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useGraph, type GraphFocus } from "@/features/catalog/api/graph";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };

function parseRef(token: string | undefined | null): GraphFocus | null {
  if (!token) return null;
  const [kind, id] = token.split(":");
  if ((kind === "application" || kind === "service") && id) return { kind: kind as RelationshipKind, id };
  return null;
}

export function GraphExplorerPage() {
  const [params, setParams] = useSearchParams();

  const focus = parseRef(params.get("focus"));
  const expandTokens = (params.get("expand") ?? "").split(",").filter(Boolean);
  const expand = expandTokens.map(parseRef).filter((r): r is GraphFocus => r !== null);

  // useGraph must be called unconditionally; pass a harmless placeholder when focus is absent.
  const safeFocus = focus ?? { kind: "application" as RelationshipKind, id: "" };
  const { results, isLoading, isError } = useGraph({ focus: safeFocus, expand });

  const focusId = focus ? `${focus.kind}:${focus.id}` : "";
  const { nodes, edges } = useMemo(() => {
    if (!focus) return { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] };
    return layoutGraph(mergeGraphs(results), focusId);
  }, [results, focus, focusId]);

  function toggleExpand(id: string) {
    if (id === focusId) return; // focus node is the root
    const set = new Set(expandTokens);
    if (set.has(id)) set.delete(id);
    else set.add(id);
    const next = new URLSearchParams(params);
    if (set.size) next.set("expand", [...set].join(","));
    else next.delete("expand");
    setParams(next);
  }

  if (!focus) {
    return <div className="p-8 text-sm text-tertiary">Pick an entity to explore its dependency graph.</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] flex-col gap-2 p-4">
      <h1 className="text-lg font-semibold text-primary">Dependency graph</h1>
      {isLoading ? (
        <Skeleton className="h-full w-full" />
      ) : isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
      ) : (
        <>
          {results.some((r) => r.truncated) && (
            <p className="text-xs text-tertiary">Showing a partial graph (node limit reached) — refine your focus to see more.</p>
          )}
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
              onNodeClick={(_, node) => toggleExpand(node.id)}
            >
              <Background />
              <Controls showInteractive={false} />
              <MiniMap pannable zoomable />
            </ReactFlow>
          </div>
        </>
      )}
    </div>
  );
}
