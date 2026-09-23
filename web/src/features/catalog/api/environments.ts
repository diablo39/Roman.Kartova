import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type EnvironmentListItemResponse = components["schemas"]["EnvironmentListItemResponse"];
type EnvironmentDetailResponse = components["schemas"]["EnvironmentDetailResponse"];
type RegisterEnvironmentRequest = components["schemas"]["RegisterEnvironmentRequest"];

// NB: like ListVms/ListInfrastructure, the generated ListEnvironments query
// types sortBy/sortOrder/limit as bare `string` (no backend enum annotation
// yet on this newer endpoint) — so these param types resolve to `string`,
// not a literal union. Callers still get drift protection from the `query`
// object shape itself; `limit` must be sent as a string to match the
// generated type.
type ListEnvironmentsQuery = NonNullable<operations["ListEnvironments"]["parameters"]["query"]>;

type EnvironmentListParams = {
  sortBy: NonNullable<ListEnvironmentsQuery["sortBy"]>;
  sortOrder: NonNullable<ListEnvironmentsQuery["sortOrder"]>;
  limit?: number;
  /** Environment-type multi-select (wire values, e.g. "production"). Empty/undefined ⇒ omitted. */
  type?: string[];
  region?: string;
  displayNameContains?: string;
};

export const envKeys = {
  all: ["environments"] as const,
  list: (params?: EnvironmentListParams) =>
    params
      ? ([...envKeys.all, "list", params] as const)
      : ([...envKeys.all, "list"] as const),
  detail: (id: string) => [...envKeys.all, "detail", id] as const,
};

export function useEnvironmentsList(params: EnvironmentListParams) {
  return useCursorList<EnvironmentListItemResponse>({
    queryKey: envKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/environments", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: String(params.limit ?? 50),
            cursor,
            ...(params.type?.length ? { type: params.type } : {}),
            ...(params.region ? { region: params.region } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useEnvironment(id: string) {
  return useQuery({
    queryKey: envKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/environments/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterEnvironment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterEnvironmentRequest) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/environments", {
        body: input,
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: envKeys.all });
    },
  });
}

export type { EnvironmentListItemResponse, EnvironmentDetailResponse, RegisterEnvironmentRequest };
