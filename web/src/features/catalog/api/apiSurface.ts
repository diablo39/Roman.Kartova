import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type ApiSurfaceResponse = components["schemas"]["ApiSurfaceResponse"];
export type ApiSurfaceItem = components["schemas"]["ApiSurfaceItem"];

export function useApiSurface(entityKind: "service" | "application", entityId: string) {
  return useQuery({
    queryKey: ["catalog", "api-surface", entityKind, entityId],
    enabled: entityId !== "",
    queryFn: async (): Promise<ApiSurfaceResponse> => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/api-surface", {
        params: { query: { entityKind, entityId } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
