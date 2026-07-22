import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterSystemInput } from "../schemas/registerSystem";
import type { components, operations } from "@/generated/openapi";

type SystemResponse = components["schemas"]["SystemResponse"];
type ListSystemsQuery = NonNullable<operations["ListSystems"]["parameters"]["query"]>;

type SystemsListParams = {
  sortBy: NonNullable<ListSystemsQuery["sortBy"]>;      // "createdAt" | "displayName"
  sortOrder: NonNullable<ListSystemsQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 steward-team multi-select. Empty/undefined ⇒ omitted ⇒ show all. */
  teamId?: string[];
  displayNameContains?: string;
};

export const systemKeys = {
  all: ["systems"] as const,
  list: (params?: SystemsListParams) =>
    params ? ([...systemKeys.all, "list", params] as const) : ([...systemKeys.all, "list"] as const),
  detail: (id: string) => [...systemKeys.all, "detail", id] as const,
};

export function useSystemsList(params: SystemsListParams) {
  return useCursorList<SystemResponse>({
    queryKey: systemKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/systems", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
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

export function useSystem(id: string) {
  return useQuery({
    queryKey: systemKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/systems/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterSystem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterSystemInput) => {
      const body = { ...input, description: input.description?.trim() ? input.description : undefined };
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/systems", { body });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: systemKeys.all });
    },
  });
}

export type { SystemResponse };
