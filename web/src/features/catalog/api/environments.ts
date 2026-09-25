import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type EnvironmentListItemResponse = components["schemas"]["EnvironmentListItemResponse"];
type EnvironmentDetailResponse = components["schemas"]["EnvironmentDetailResponse"];
type RegisterEnvironmentRequest = components["schemas"]["RegisterEnvironmentRequest"];
type EditEnvironmentRequest = components["schemas"]["EditEnvironmentRequest"];

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

/**
 * PUT /environments/{id} — full-replacement edit (A2). Mirrors `useEditVm`: the
 * If-Match header carries the optimistic-concurrency token straight from the cached
 * `version` field on `EnvironmentDetailResponse`. On 412 the hook invalidates the
 * detail query so the dialog auto-refreshes.
 */
export function useEditEnvironment(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { values: EditEnvironmentRequest; expectedVersion: string }) => {
      const { data, error, response } = await apiClient.PUT("/api/v1/catalog/environments/{id}", {
        params: { path: { id } },
        body: input.values,
        headers: { "If-Match": `"${input.expectedVersion}"` },
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data, response);
    },
    onSuccess: (data) => {
      qc.setQueryData(envKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: envKeys.list() });
    },
    onError: (err) => {
      const status = (err as { __status?: number }).__status;
      if (status === 412) {
        qc.invalidateQueries({ queryKey: envKeys.detail(id) });
      }
    },
  });
}

/**
 * DELETE /environments/{id} — hard delete (A2). Same If-Match convention as
 * `useEditEnvironment`; no `onSuccess` cache write since the resource is gone — the
 * caller navigates away and the invalidated list queries refetch.
 */
export function useDeleteEnvironment(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (expectedVersion: string) => {
      const { error, response } = await apiClient.DELETE("/api/v1/catalog/environments/{id}", {
        params: { path: { id } },
        headers: { "If-Match": `"${expectedVersion}"` },
      });
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: envKeys.all });
    },
  });
}

export type { EnvironmentListItemResponse, EnvironmentDetailResponse, RegisterEnvironmentRequest, EditEnvironmentRequest };
