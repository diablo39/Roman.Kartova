// web/src/features/catalog/api/graph.ts
import { useQueries } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";
import type { ExpandEntry } from "@/features/catalog/relationships/useExplorerState";

export type GraphResponse = components["schemas"]["GraphResponse"];
export type GraphFocus = { kind: RelationshipKind; id: string };

const FOCUS_DEPTH = 2;
const EXPAND_DEPTH = 1;

type GraphDirection = "outgoing" | "incoming" | "all";

function parseNode(node: string): GraphFocus {
  const [kind, id] = node.split(":");
  return { kind: kind as RelationshipKind, id: id ?? "" };
}

export const graphKeys = {
  all: ["catalog", "graph"] as const,
  node: (f: GraphFocus, depth: number, direction: GraphDirection) =>
    [...graphKeys.all, f.kind, f.id, depth, direction] as const,
};

async function fetchGraph(f: GraphFocus, depth: number, direction: GraphDirection): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/graph", {
    params: { query: { entityKind: f.kind, entityId: f.id, depth, direction } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useGraph({ focus, expand }: { focus: GraphFocus; expand: ExpandEntry[] }) {
  const queries = useQueries({
    queries: [
      { queryKey: graphKeys.node(focus, FOCUS_DEPTH, "all"), queryFn: () => fetchGraph(focus, FOCUS_DEPTH, "all") },
      ...expand.map((e) => {
        const f = parseNode(e.node);
        const direction: GraphDirection = e.dir === "out" ? "outgoing" : "incoming";
        return {
          queryKey: graphKeys.node(f, EXPAND_DEPTH, direction),
          queryFn: () => fetchGraph(f, EXPAND_DEPTH, direction),
        };
      }),
    ],
  });
  return {
    results: queries.map((q) => q.data).filter((d): d is GraphResponse => !!d),
    isLoading: queries.some((q) => q.isLoading),
    isError: queries.some((q) => q.isError),
    refetch: () => queries.forEach((q) => q.refetch()),
  };
}
