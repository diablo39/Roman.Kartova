import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";

export const orgKeys = { me: ["organization", "me"] as const };

export function useCurrentOrganization() {
  return useQuery({
    queryKey: orgKeys.me,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/me");
      if (error) throw error;
      return data!;
    },
    staleTime: 5 * 60 * 1000,
  });
}
