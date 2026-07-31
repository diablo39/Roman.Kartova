import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import { relationshipKeys, useRelationshipsList } from "./relationships";
import type { RegisterSystemInput } from "../schemas/registerSystem";
import type { components, operations } from "@/generated/openapi";

type SystemResponse = components["schemas"]["SystemResponse"];
type SystemMembership = components["schemas"]["SystemMembershipResponse"];
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
      // RegisterSystemRequest.description is `string | null` in the generated contract (required-nullable),
      // so a blank/absent description is sent as null (not undefined).
      const body = { ...input, description: input.description?.trim() ? input.description : null };
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/systems", { body });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: systemKeys.all });
    },
  });
}

export type { SystemResponse, SystemMembership };

export type ComponentKind = "application" | "service";

/**
 * The System a component currently belongs to, read with a SERVER-side `type=partOf` filter
 * (Task 4d). Do not read page 1 of the unfiltered list and filter here: a component with more
 * than `limit` outgoing edges would render "Not assigned" while its membership exists.
 * `limit: 1` is honest — at-most-one is a DB invariant (ADR-0111 amended).
 */
export function useComponentSystem(componentKind: ComponentKind, componentId: string) {
  const list = useRelationshipsList({
    entityKind: componentKind,
    entityId: componentId,
    direction: "outgoing",
    type: "partOf",
    limit: 1,
  });
  const edge = list.items.find((item) => item.type === "partOf");
  return {
    systemId: edge?.target.id ?? null,
    systemDisplayName: edge?.target.displayName ?? null,
    isLoading: list.isLoading,
    isError: list.isError,
  };
}

/**
 * PUT /catalog/{applications|services}/{id}/system — atomic set / move / clear of the
 * component's System (`systemId: null` clears). Invalidates the relationship family and
 * every derived `["catalog", …]` read model (members list, mini-graph, graph, impact).
 */
export function useSetComponentSystem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: {
      componentKind: ComponentKind;
      componentId: string;
      systemId: string | null;
    }): Promise<SystemMembership> => {
      const body = { systemId: input.systemId };
      if (input.componentKind === "application") {
        const { data, error, response } = await apiClient.PUT("/api/v1/catalog/applications/{id}/system", {
          params: { path: { id: input.componentId } },
          body,
        });
        if (error) throwWithStatus(error, response);
        return unwrapData(data);
      }
      const { data, error, response } = await apiClient.PUT("/api/v1/catalog/services/{id}/system", {
        params: { path: { id: input.componentId } },
        body,
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: relationshipKeys.all });
      qc.invalidateQueries({ queryKey: ["catalog"] });
      qc.invalidateQueries({ queryKey: systemKeys.all });
    },
  });
}
