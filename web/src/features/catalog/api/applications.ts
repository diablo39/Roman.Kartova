import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import type { RegisterApplicationInput } from "../schemas/registerApplication";
import type { components, operations } from "@/generated/openapi";

type ApplicationResponse = components["schemas"]["ApplicationResponse"];

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
