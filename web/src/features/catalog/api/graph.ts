// web/src/features/catalog/api/graph.ts
import { useQueries } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphResponse = components["schemas"]["GraphResponse"];
export type GraphFocus = { kind: RelationshipKind; id: string };

const FOCUS_DEPTH = 2;
const EXPAND_DEPTH = 1;

export const graphKeys = {
  all: ["catalog", "graph"] as const,
  node: (f: GraphFocus, depth: number) => [...graphKeys.all, f.kind, f.id, depth] as const,
};

async function fetchGraph(f: GraphFocus, depth: number): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/graph", {
    params: { query: { entityKind: f.kind, entityId: f.id, depth, direction: "all" } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useGraph({ focus, expand }: { focus: GraphFocus; expand: GraphFocus[] }) {
  const queries = useQueries({
    queries: [
      { queryKey: graphKeys.node(focus, FOCUS_DEPTH), queryFn: () => fetchGraph(focus, FOCUS_DEPTH) },
      ...expand.map((n) => ({
        queryKey: graphKeys.node(n, EXPAND_DEPTH),
        queryFn: () => fetchGraph(n, EXPAND_DEPTH),
      })),
    ],
  });
  return {
    results: queries.map((q) => q.data).filter((d): d is GraphResponse => !!d),
    isLoading: queries.some((q) => q.isLoading),
    isError: queries.some((q) => q.isError),
    refetch: () => queries.forEach((q) => q.refetch()),
  };
}
