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
  /** ADR-0073 default-view rule: false (the default) hides Decommissioned rows. Slice 6. */
  includeDecommissioned?: boolean;
};

export const applicationKeys = {
  all: ["applications"] as const,
  list: (params?: ApplicationsListParams) =>
    params
      ? ([...applicationKeys.all, "list", params] as const)
      : ([...applicationKeys.all, "list"] as const),
  detail: (id: string) => [...applicationKeys.all, "detail", id] as const,
};

/**
 * Re-throws an openapi-fetch error after attaching the HTTP status as a
 * `__status` field so callers can branch on 412 / 409 / 400 without re-parsing
 * the response. Used by every mutation that maps RFC 7807 problem types to
 * dialog UX.
 */
function throwWithStatus(error: unknown, response: { status: number }): never {
  (error as Record<string, unknown>).__status = response.status;
  throw error;
}

function unwrapData<T>(data: T | undefined): T {
  if (!data) throw new Error("API returned neither data nor error");
  return data;
}

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
            includeDecommissioned: params.includeDecommissioned ?? false,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
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
      return unwrapData(data);
    },
  });
}

export function useRegisterApplication() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApplicationInput) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/applications", { body: input });
      if (error) throw error;
      return unwrapData(data);
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
 * (the version is wrapped in double quotes for the wire — `If-Match: "v1"` —
 * per RFC 7232 §2.3 strong-ETag syntax). On 412 the hook invalidates the
 * detail query so the dialog auto-refreshes from RHF `values` next render
 * (spec §8.3).
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
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      // Detail is already refreshed via setQueryData; only list views need a
      // refetch to pick up the new displayName / version.
      qc.invalidateQueries({ queryKey: applicationKeys.list() });
    },
    onError: (err) => {
      // Spec §8.3 — on 412 ConcurrencyConflict the dialog stays open with
      // refreshed pre-fill (RHF `values` resets when the parent re-renders
      // with the freshly fetched application). Invalidating the detail query
      // here triggers that refetch + re-render. Other statuses are handled
      // by the dialog's catch branch.
      const status = (err as { __status?: number }).__status;
      if (status === 412) {
        qc.invalidateQueries({ queryKey: applicationKeys.detail(id) });
      }
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
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.list() });
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
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.list() });
    },
  });
}

export type { ApplicationResponse, Lifecycle };
