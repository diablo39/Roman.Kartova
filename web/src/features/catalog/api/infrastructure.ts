import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type VmListItemResponse = components["schemas"]["VmListItemResponse"];
type InfrastructureListItemResponse = components["schemas"]["InfrastructureListItemResponse"];
type VmDetailResponse = components["schemas"]["VmDetailResponse"];
type RegisterVmRequest = components["schemas"]["RegisterVmRequest"];

// NB: unlike ListServices, the generated ListVms/ListInfrastructure query types
// type sortBy/sortOrder/limit as bare `string` (no backend enum annotation yet
// on these newer endpoints) — so these param types resolve to `string`, not a
// literal union. Callers still get drift protection from the `query` object
// shape itself; `limit` must be sent as a string to match the generated type.
type ListVmsQuery = NonNullable<operations["ListVms"]["parameters"]["query"]>;
type ListInfrastructureQuery = NonNullable<operations["ListInfrastructure"]["parameters"]["query"]>;

type VmListParams = {
  sortBy: NonNullable<ListVmsQuery["sortBy"]>;
  sortOrder: NonNullable<ListVmsQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ no predicate (show all). */
  teamId?: string[];
  powerState?: string;
  os?: string;
  region?: string;
  hostname?: string;
  ipAddress?: string;
};

type InfrastructureListParams = {
  sortBy: NonNullable<ListInfrastructureQuery["sortBy"]>;
  sortOrder: NonNullable<ListInfrastructureQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ no predicate (show all). */
  teamId?: string[];
  /** Infrastructure-type multi-select (wire values, e.g. "virtualMachine"). Empty/undefined ⇒ omitted. */
  type?: string[];
};

export const infraKeys = {
  all: ["infrastructure"] as const,
  list: (params?: InfrastructureListParams) =>
    params
      ? ([...infraKeys.all, "list", params] as const)
      : ([...infraKeys.all, "list"] as const),
  vmList: (params?: VmListParams) =>
    params
      ? ([...infraKeys.all, "vms", "list", params] as const)
      : ([...infraKeys.all, "vms", "list"] as const),
  detail: (id: string) => [...infraKeys.all, "vms", "detail", id] as const,
};

export function useVmList(params: VmListParams) {
  return useCursorList<VmListItemResponse>({
    queryKey: infraKeys.vmList(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/infrastructure/vms", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: String(params.limit ?? 50),
            cursor,
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.powerState ? { powerState: params.powerState } : {}),
            ...(params.os ? { os: params.os } : {}),
            ...(params.region ? { region: params.region } : {}),
            ...(params.hostname ? { hostname: params.hostname } : {}),
            ...(params.ipAddress ? { ipAddress: params.ipAddress } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useInfrastructureList(params: InfrastructureListParams) {
  return useCursorList<InfrastructureListItemResponse>({
    queryKey: infraKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/infrastructure", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: String(params.limit ?? 50),
            cursor,
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.type?.length ? { type: params.type } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useVm(id: string) {
  return useQuery({
    queryKey: infraKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/infrastructure/vms/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterVm() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterVmRequest) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/infrastructure/vms", {
        body: input,
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: infraKeys.all });
    },
  });
}

export type { VmListItemResponse, InfrastructureListItemResponse, VmDetailResponse, RegisterVmRequest };
