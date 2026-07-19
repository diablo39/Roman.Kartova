import { createContext, useContext } from "react";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphActions = {
  toggleExpand: (node: string, dir: ExpandDir) => void;
  setFocus: (kind: RelationshipKind, id: string) => void;
  openPage: (kind: RelationshipKind, id: string) => void;
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
