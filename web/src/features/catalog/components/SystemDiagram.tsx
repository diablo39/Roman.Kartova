import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ReactFlow, Background, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Toggle } from "@/components/base/toggle/toggle";
import { useGraph } from "@/features/catalog/api/graph";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { systemMemberIds, systemBoundaryBox } from "@/features/catalog/relationships/systemBoundary";
import { SystemBoundaryNode, type SystemBoundaryData } from "@/features/catalog/components/SystemBoundaryNode";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphActionsProvider } from "@/features/catalog/relationships/GraphActionsContext";
import { entityDetailPath, graphFocusPath, type GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { EntityKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode, systemBoundary: SystemBoundaryNode };

interface Props {
  systemId: string;
  displayName: string;
}

export function SystemDiagram({ systemId, displayName }: Props) {
  const navigate = useNavigate();
  const [includeExternal, setIncludeExternal] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const focusId = `system:${systemId}`;
  const graph = useGraph({
    focus: { kind: "system", id: systemId },
    expand: [],
    depth: includeExternal ? 2 : 1,
  });

  // Matches the standalone /graph explorer's interaction: a node click SELECTS (highlights)
  // rather than navigating; navigation is an explicit "Open page ↗" in the node's ⋯ menu.
  const actions = useMemo(
    () => ({
      // Fixed-depth diagram: not expandable, so the ⋯ menu drops its Expand items (supportsExpand).
      toggleExpand: () => {},
      setFocus: (kind: EntityKind, id: string) => navigate(graphFocusPath(kind, id)),
      openPage: (kind: EntityKind, id: string) => navigate(entityDetailPath(kind, id)),
      atCap: false,
      supportsExpand: false,
    }),
    [navigate],
  );

  const { nodes, edges, memberCount, truncated } = useMemo(() => {
    const merged = mergeGraphs(graph.results);
    const memberIds = systemMemberIds(merged, focusId);
    const laid = layoutGraph(merged, focusId, selectedId);
    const box = systemBoundaryBox(laid.nodes, memberIds, focusId);

    // partOf edges stay in the dagre input above (they anchor member ranks next to the System
    // node) but are not drawn — the band states membership, drawing it again is noise.
    const partOfIds = new Set(merged.edges.filter((e) => e.type === "partOf").map((e) => e.id));
    const visibleEdges = laid.edges.filter((e) => !partOfIds.has(e.id));

    const bandNode: Node<SystemBoundaryData>[] = box
      ? [
          {
            id: "system-boundary",
            type: "systemBoundary",
            position: { x: box.x, y: box.y },
            data: { label: displayName, width: box.width, height: box.height },
            draggable: false,
            selectable: false,
            focusable: false,
            zIndex: -1,
          },
        ]
      : [];

    return {
      nodes: [...bandNode, ...laid.nodes],
      edges: visibleEdges,
      memberCount: memberIds.size,
      truncated: merged.truncated,
    };
  }, [graph.results, focusId, selectedId, displayName]);

  return (
    <section className="space-y-2" aria-label="System diagram">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-primary">System diagram</h3>
        <div className="flex items-center gap-4">
          <Toggle label="Include external dependencies" isSelected={includeExternal} onChange={setIncludeExternal} />
          <Link to={graphFocusPath("system", systemId)} className="text-xs text-brand-secondary underline">
            Open full graph ↗
          </Link>
        </div>
      </div>
      {graph.isLoading ? (
        <Skeleton className="h-80 w-full" />
      ) : graph.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the system diagram.</p>
      ) : memberCount === 0 ? (
        <p className="text-sm italic text-tertiary">No members yet.</p>
      ) : (
        <>
          <div className="h-80 w-full overflow-hidden rounded-lg ring-1 ring-secondary">
            <GraphActionsProvider value={actions}>
              <ReactFlow
                nodes={nodes as Node[]}
                edges={edges as Edge[]}
                nodeTypes={NODE_TYPES}
                fitView
                nodesDraggable={false}
                nodesConnectable={false}
                elementsSelectable={false}
                proOptions={{ hideAttribution: true }}
                onNodeClick={(_, node) => {
                  if (node.type === "systemBoundary") return;
                  const data = node.data as GraphNodeData;
                  if (data.side === "focused") return;
                  setSelectedId(node.id);
                }}
              >
                <Background />
              </ReactFlow>
            </GraphActionsProvider>
          </div>
          {truncated && (
            <p className="text-xs text-warning-primary">Showing only the first nodes of a large system.</p>
          )}
        </>
      )}
    </section>
  );
}
