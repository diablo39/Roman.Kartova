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
};

export const relationshipKeys = {
  all: ["relationships"] as const,
  list: (p?: RelationshipsListParams) =>
    p
      ? ([...relationshipKeys.all, "list", p] as const)
      : ([...relationshipKeys.all, "list"] as const),
};

export function useRelationshipsList(params: RelationshipsListParams) {
  return useCursorList<RelationshipResponse>({
    queryKey: relationshipKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/relationships", {
        params: {
          query: {
            entityKind: params.entityKind,
            entityId: params.entityId,
            direction: params.direction,
            limit: String(params.limit ?? 20),
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useCreateRelationship() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: CreateRelationshipPayload) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/relationships", { body: payload });
      if (error) throw error;
      return unwrapData(data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: relationshipKeys.all }),
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
    onSuccess: () => qc.invalidateQueries({ queryKey: relationshipKeys.all }),
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
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", { params: { query: q } });
      if (error) throw error;
      return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
    },
  });
}
