import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

type CatalogHierarchyResponse = components["schemas"]["CatalogHierarchyResponse"];

export const hierarchyKeys = { all: ["catalog", "hierarchy"] as const };

export function useCatalogHierarchy() {
  return useQuery<CatalogHierarchyResponse>({
    queryKey: hierarchyKeys.all,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/hierarchy");
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
