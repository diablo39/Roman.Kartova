import { createContext, useContext } from "react";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";
import type { EntityKind } from "@/features/catalog/relationships/relationshipTypeRules";
import { entityDetailPath, graphFocusPath } from "@/features/catalog/relationships/graphModel";

export type GraphActions = {
  toggleExpand: (node: string, dir: ExpandDir) => void;
  setFocus: (kind: EntityKind, id: string) => void;
  openPage: (kind: EntityKind, id: string) => void;
  atCap: boolean;
  /**
   * Whether nodes support expand/collapse. Omitted (or `true`) in the full graph explorer.
   * The mini-graph is a fixed 1-hop preview, so it passes `false` to drop the (otherwise
   * permanently disabled) "Expand" items from each node's ⋯ menu.
   */
  supportsExpand?: boolean;
};

const noop = () => {};
const GraphActionsContext = createContext<GraphActions>({
  toggleExpand: noop,
  setFocus: noop,
  openPage: noop,
  atCap: false,
});

export const GraphActionsProvider = GraphActionsContext.Provider;
export const useGraphActions = () => useContext(GraphActionsContext);

/**
 * Shared shape for a fixed-depth, read-only preview graph (SystemDiagram, DependencyMiniGraph):
 * no expand/collapse, no cap, and navigation delegated to the caller's router.
 */
export function createReadOnlyGraphActions(navigate: (path: string) => void): GraphActions {
  return {
    toggleExpand: noop,
    setFocus: (kind, id) => navigate(graphFocusPath(kind, id)),
    openPage: (kind, id) => navigate(entityDetailPath(kind, id)),
    atCap: false,
    supportsExpand: false,
  };
}
