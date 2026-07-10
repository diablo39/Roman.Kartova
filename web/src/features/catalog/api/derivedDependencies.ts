import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type DerivedDependenciesResponse = components["schemas"]["DerivedDependenciesResponse"];
export type DerivedDependencyItem = components["schemas"]["DerivedDependencyItem"];

export function useDerivedDependencies(entityId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["catalog", "derived-dependencies", entityId],
    enabled: entityId !== "" && (options?.enabled ?? true),
    queryFn: async (): Promise<DerivedDependenciesResponse> => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/derived-dependencies", {
        params: { query: { entityId } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
