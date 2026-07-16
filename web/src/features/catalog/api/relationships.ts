import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type RelationshipResponse = components["schemas"]["RelationshipResponse"];
export type CreateRelationshipPayload = components["schemas"]["CreateRelationshipRequest"];
type ListQuery = NonNullable<operations["ListRelationships"]["parameters"]["query"]>;
export type RelationshipDirection = NonNullable<ListQuery["direction"]>;

export type RelationshipsListParams = {
  entityKind: NonNullable<ListQuery["entityKind"]>;
  entityId: string;
  direction: RelationshipDirection;
  limit?: number;
  excludeApiEdges?: boolean;
};

export const relationshipKeys = {
  all: ["relationships"] as const,
  list: (p?: RelationshipsListParams) =>
    p
      ? ([...relationshipKeys.all, "list", p] as const)
      : ([...relationshipKeys.all, "list"] as const),
};

export function useRelationshipsList(
  params: RelationshipsListParams,
  opts?: { enabled?: boolean },
) {
  return useCursorList<RelationshipResponse>({
    queryKey: relationshipKeys.list(params),
    enabled: opts?.enabled,
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/relationships", {
        params: {
          query: {
            entityKind: params.entityKind,
            entityId: params.entityId,
            direction: params.direction,
            limit: String(params.limit ?? 20),
            cursor,
            ...(params.excludeApiEdges ? { excludeApiEdges: true } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

// A relationship change also feeds every derived read model keyed under
// ["catalog", ...] — the API surface (provides/consumes), the dependency graph,
// derived dependencies, and impact analysis. Invalidate both families so those
// sections refresh without a manual page reload.
function invalidateAfterRelationshipChange(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: relationshipKeys.all });
  qc.invalidateQueries({ queryKey: ["catalog"] });
}

export function useCreateRelationship() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: CreateRelationshipPayload) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/relationships", { body: payload });
      if (error) throw error;
      return unwrapData(data);
    },
    onSuccess: () => invalidateAfterRelationshipChange(qc),
  });
}

export function useDeleteRelationship() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.DELETE("/api/v1/catalog/relationships/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
    },
    onSuccess: () => invalidateAfterRelationshipChange(qc),
  });
}

export type EntityOption = { kind: RelationshipKind; id: string; displayName: string };

export function useEntitySearch(
  kind: RelationshipKind,
  query: string,
  opts: { enabled: boolean },
) {
  return useQuery({
    queryKey: ["catalog", "entity-search", kind, query],
    enabled: opts.enabled,
    queryFn: async (): Promise<EntityOption[]> => {
      const q = { displayNameContains: query, sortBy: "displayName", sortOrder: "asc", limit: 10 } as const;
      if (kind === "application") {
        const { data, error } = await apiClient.GET("/api/v1/catalog/applications", { params: { query: q } });
        if (error) throw error;
        return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
      }
      if (kind === "api") {
        const { data, error } = await apiClient.GET("/api/v1/catalog/apis", { params: { query: q } });
        if (error) throw error;
        return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
      }
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", { params: { query: q } });
      if (error) throw error;
      return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
    },
  });
}
