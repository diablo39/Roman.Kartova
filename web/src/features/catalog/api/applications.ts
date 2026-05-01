import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import type { RegisterApplicationInput } from "../schemas/registerApplication";

export const applicationKeys = {
  all: ["applications"] as const,
  list: () => [...applicationKeys.all, "list"] as const,
  detail: (id: string) => [...applicationKeys.all, "detail", id] as const,
};

export function useApplications() {
  return useQuery({
    queryKey: applicationKeys.list(),
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/applications");
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
      const { data, error } = await apiClient.POST(
        "/api/v1/catalog/applications",
        { body: input }
      );
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: applicationKeys.list() });
    },
  });
}
