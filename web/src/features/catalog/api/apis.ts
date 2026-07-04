import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterApiInput } from "../schemas/registerApi";
import type { components, operations } from "@/generated/openapi";

type ApiResponse = components["schemas"]["ApiResponse"];
type ListApisQuery = NonNullable<operations["ListApis"]["parameters"]["query"]>;

type ApisListParams = {
  sortBy: NonNullable<ListApisQuery["sortBy"]>;      // "displayName" | "style" | "version" | "createdAt"
  sortOrder: NonNullable<ListApisQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 style multi-select (wire values rest|grpc|graphQL). Empty/undefined ⇒ omitted ⇒ show all. */
  style?: string[];
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ show all. */
  teamId?: string[];
  displayNameContains?: string;
};

export const apiKeys = {
  all: ["apis"] as const,
  list: (params?: ApisListParams) =>
    params ? ([...apiKeys.all, "list", params] as const) : ([...apiKeys.all, "list"] as const),
  detail: (id: string) => [...apiKeys.all, "detail", id] as const,
};

export function useApisList(params: ApisListParams) {
  return useCursorList<ApiResponse>({
    queryKey: apiKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.style?.length ? { style: params.style } : {}),
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useApi(id: string) {
  return useQuery({
    queryKey: apiKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis/{id}", { params: { path: { id } } });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterApi() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApiInput) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/apis", {
        body: { ...input, specUrl: input.specUrl || null },
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: apiKeys.all });
    },
  });
}

export type { ApiResponse };
