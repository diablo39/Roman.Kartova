import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterServiceInput } from "../schemas/registerService";
import type { components, operations } from "@/generated/openapi";

type ServiceResponse = components["schemas"]["ServiceResponse"];
type ListServicesQuery = NonNullable<operations["ListServices"]["parameters"]["query"]>;

type ServicesListParams = {
  sortBy: NonNullable<ListServicesQuery["sortBy"]>;     // "createdAt" | "displayName"
  sortOrder: NonNullable<ListServicesQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ no predicate (show all). */
  teamId?: string[];
  /** ADR-0107 health multi-select (wire values unknown|healthy|degraded|unhealthy).
   *  Empty/undefined ⇒ omitted ⇒ no predicate (show all health statuses). */
  health?: string[];
  displayNameContains?: string;
};

export const serviceKeys = {
  all: ["services"] as const,
  list: (params?: ServicesListParams) =>
    params
      ? ([...serviceKeys.all, "list", params] as const)
      : ([...serviceKeys.all, "list"] as const),
  detail: (id: string) => [...serviceKeys.all, "detail", id] as const,
};

export function useServicesList(params: ServicesListParams) {
  return useCursorList<ServiceResponse>({
    queryKey: serviceKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.health?.length ? { health: params.health } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useService(id: string) {
  return useQuery({
    queryKey: serviceKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/services/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterService() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterServiceInput) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/services", { body: input });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: serviceKeys.all });
    },
  });
}

export type { ServiceResponse };
