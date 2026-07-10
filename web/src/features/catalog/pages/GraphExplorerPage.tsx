import { useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ReactFlow, Background, Controls, MiniMap, Panel, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useGraph } from "@/features/catalog/api/graph";
import { useImpactAnalysis, type ImpactSubject } from "@/features/catalog/api/impact";
import { mergeGraphs, bfsDepth, computeAffordance } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { useExplorerState } from "@/features/catalog/relationships/useExplorerState";
import { buildTierMap, impactDim, tierCounts, impactTotal } from "@/features/catalog/relationships/impactModel";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";
import { ImpactBanner } from "@/features/catalog/components/ImpactBanner";
import { GraphActionsProvider, type GraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { parseEntityRef, entityDetailPath, graphFocusPath } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";
import { useGraphFilters } from "@/features/catalog/relationships/useGraphFilters";
import { applyGraphFilters } from "@/features/catalog/relationships/graphFilter";
import { GraphFilterControls } from "@/features/catalog/components/GraphFilterControls";
import { useTeamsList } from "@/features/teams/api/teams";

const NODE_TYPES = { entity: EntityGraphNode };
const SOFT_CAP = 150;

export function GraphExplorerPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const focus = parseEntityRef(params.get("focus"));
  const focusId = focus ? `${focus.kind}:${focus.id}` : "";
  const { expand, selected, isExpanded, toggleExpand, select, reset } = useExplorerState(focusId);
  const { filters, setKinds, setTeamIds, clear, activeCount } = useGraphFilters(focusId);
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });

  const safeFocus = focus ?? { kind: "application" as RelationshipKind, id: "" };
  const { results, isLoading, isError, expandError, refetch } = useGraph({ focus: safeFocus, expand });

  const [impactSubject, setImpactSubject] = useState<ImpactSubject | null>(null);
  // Clear a stale impact overlay when the focus changes (prev-key guard, matching useExplorerState).
  const [prevFocusForImpact, setPrevFocusForImpact] = useState(focusId);
  if (prevFocusForImpact !== focusId) {
    setPrevFocusForImpact(focusId);
    setImpactSubject(null);
  }
  const impact = useImpactAnalysis(impactSubject);
  const impactResult = impact.data ?? null;
  const impactActive = impactSubject != null && impactResult != null;

  const merged = useMemo(
    () => (focusId ? mergeGraphs(impactResult ? [...results, impactResult] : results) : { nodes: [], edges: [], truncated: false }),
    [results, impactResult, focusId],
  );

  const tierByNodeId = useMemo(() => (impactResult ? buildTierMap(impactResult) : null), [impactResult]);

  const atCap = merged.nodes.length >= SOFT_CAP;
  const dimmed = useMemo(() => {
    if (impactActive && impactResult) {
      // Impact overlay supersedes filters for the impacted set: the banner's count must
      // always equal the number of glowing (non-dimmed) nodes, regardless of active filters.
      const impactIds = new Set(impactResult.nodes.map((n) => `${n.kind}:${n.id}`));
      return impactDim(merged, impactIds);
    }
    return applyGraphFilters(merged, filters, focusId);
  }, [merged, filters, focusId, impactActive, impactResult]);
  const decorate = useMemo(() => computeAffordance(merged, isExpanded), [merged, isExpanded]);
  const { nodes, edges } = useMemo(
    () =>
      focusId
        ? layoutGraph(merged, focusId, selected, { nodeIds: dimmed.dimmedNodeIds, edgeIds: dimmed.dimmedEdgeIds }, decorate, tierByNodeId ?? undefined)
        : { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] },
    [merged, focusId, selected, dimmed, decorate, tierByNodeId],
  );
  const actions = useMemo<GraphActions>(() => ({
    toggleExpand,
    setFocus: (kind, id) => navigate(graphFocusPath(kind, id)),
    openPage: (kind, id) => navigate(entityDetailPath(kind, id)),
    atCap,
  }), [toggleExpand, navigate, atCap]);

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
              <GraphActionsProvider value={actions}>
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
                  <Panel position="top-left">
                    <GraphFilterControls
                      kinds={filters.kinds}
                      teamIds={filters.teamIds}
                      teams={teamsList.items ?? []}
                      activeCount={activeCount}
                      onKindsChange={setKinds}
                      onTeamIdsChange={setTeamIds}
                      onClear={clear}
                    />
                  </Panel>
                  <Panel position="bottom-left">
                    <div className="rounded-md bg-primary/90 px-2 py-1 text-xs text-tertiary ring-1 ring-secondary">
                      <span className="mr-3">— explicit</span>
                      <span className="font-mono">- - derived</span>
                    </div>
                  </Panel>
                  {impactSubject != null && impact.isError && (
                    <Panel position="top-right">
                      <div className="flex items-center gap-3 rounded-md bg-primary/90 px-3 py-2 text-sm ring-1 ring-secondary">
                        <p className="text-sm text-error-primary">Couldn&apos;t run impact analysis.</p>
                        <button type="button" className="text-sm text-brand-primary underline" onClick={() => impact.refetch()}>Try again</button>
                        <button
                          type="button"
                          onClick={() => setImpactSubject(null)}
                          className="ml-2 rounded-md border border-secondary px-2 py-1 text-xs text-primary"
                        >
                          Close
                        </button>
                      </div>
                    </Panel>
                  )}
                  {impactSubject != null && !impact.isError && impact.isLoading && (
                    <Panel position="top-right">
                      <div className="flex items-center gap-3 rounded-md bg-primary/90 px-3 py-2 text-sm ring-1 ring-secondary">
                        <p className="text-sm text-tertiary">Analysing impact…</p>
                      </div>
                    </Panel>
                  )}
                  {impactActive && impactResult && tierByNodeId && (
                    <Panel position="top-right">
                      <ImpactBanner
                        total={impactTotal(tierByNodeId)}
                        tiers={tierCounts(tierByNodeId)}
                        truncated={impactResult.truncated}
                        nodeCap={200}
                        onClose={() => setImpactSubject(null)}
                      />
                    </Panel>
                  )}
                </ReactFlow>
              </GraphActionsProvider>
            </div>
            {selectedRef && (
              <GraphExplorerSidebar
                selected={selectedRef}
                depthFromFocus={depthFromFocus}
                isExpanded={isExpanded}
                atCap={atCap}
                onToggleExpand={toggleExpand}
                onSetFocus={() => navigate(graphFocusPath(selectedRef.kind, selectedRef.id))}
                onClose={() => select(null)}
                onImpactAnalysis={() => setImpactSubject(selectedRef)}
              />
            )}
          </div>
        </>
      )}
    </div>
  );
}
