import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import type { RegisterApplicationInput } from "../schemas/registerApplication";
import type { EditApplicationInput } from "../schemas/editApplication";
import type { DeprecateApplicationInput } from "../schemas/deprecateApplication";
import type { components, operations } from "@/generated/openapi";

type ApplicationResponse = components["schemas"]["ApplicationResponse"];
type Lifecycle = ApplicationResponse["lifecycle"];

// Derive sort-param types from the generated OpenAPI operation so the wire
// allowlist (createdAt|name, asc|desc) is a single source of truth — adding
// a new sort field on the backend (ADR-0095 §4.1) flows through codegen
// without requiring a hand edit here.
type ListApplicationsQuery = NonNullable<operations["ListApplications"]["parameters"]["query"]>;

type ApplicationsListParams = {
  sortBy: NonNullable<ListApplicationsQuery["sortBy"]>;
  sortOrder: NonNullable<ListApplicationsQuery["sortOrder"]>;
  limit?: number;
};

export const applicationKeys = {
  all: ["applications"] as const,
  list: (params?: ApplicationsListParams) =>
    params
      ? ([...applicationKeys.all, "list", params] as const)
      : ([...applicationKeys.all, "list"] as const),
  detail: (id: string) => [...applicationKeys.all, "detail", id] as const,
};

export function useApplicationsList(params: ApplicationsListParams) {
  return useCursorList<ApplicationResponse>({
    queryKey: applicationKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/applications", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
          },
        },
      });
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
  });
}

export function useApplication(id: string) {
  return useQuery({
    queryKey: applicationKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET(
        "/api/v1/catalog/applications/{id}",
        { params: { path: { id } } }
      );
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
  });
}

export function useRegisterApplication() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApplicationInput) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/applications", { body: input });
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: () => {
      // Invalidate the list prefix — covers all parameterized queryKeys.
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

/**
 * PUT /applications/{id} — full-replacement edit (ADR-0096). The If-Match
 * header carries the optimistic concurrency token from the cached version
 * (the strong-ETag wrapping is `"v"` per RFC 7232). On 412 the dialog is
 * expected to refetch and reapply.
 *
 * Errors are re-thrown with a non-enumerable `__status` so callers can
 * branch on 412 / 409 / 400 without re-parsing the response.
 */
export function useEditApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { values: EditApplicationInput; expectedVersion: string }) => {
      const { data, error, response } = await apiClient.PUT(
        "/api/v1/catalog/applications/{id}",
        {
          params: { path: { id } },
          body: input.values,
          headers: { "If-Match": `"${input.expectedVersion}"` },
        }
      );
      if (error) {
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

/**
 * POST /applications/{id}/deprecate — Active → Deprecated transition.
 * No If-Match: lifecycle changes are idempotent on the source state, and the
 * server enforces the transition with `409 LifecycleConflict` on a wrong
 * source state.
 */
export function useDeprecateApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: DeprecateApplicationInput) => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/catalog/applications/{id}/deprecate",
        { params: { path: { id } }, body: input }
      );
      if (error) {
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

/**
 * POST /applications/{id}/decommission — Deprecated → Decommissioned.
 * No request body, no If-Match. Server returns
 * `409` with `reason=before-sunset-date` when invoked before the configured
 * sunset date.
 */
export function useDecommissionApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/catalog/applications/{id}/decommission",
        { params: { path: { id } } }
      );
      if (error) {
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

export type { ApplicationResponse, Lifecycle };
