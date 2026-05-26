import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";

import { apiClient } from "@/features/catalog/api/client";
import type { components } from "@/generated/openapi";
import type { KartovaPermission } from "./permissions";

type MePermissionsResponse = components["schemas"]["MePermissionsResponse"];

const QUERY_KEY = ["me", "permissions"] as const;

export interface UsePermissionsResult {
  role: string | null;
  hasPermission: (perm: KartovaPermission) => boolean;
  isLoading: boolean;
  isError: boolean;
  teamIds: string[];
  teamAdminTeamIds: string[];
}

export function usePermissions(): UsePermissionsResult {
  const auth = useAuth();
  const enabled = auth.isAuthenticated;

  const query = useQuery<MePermissionsResponse>({
    queryKey: QUERY_KEY,
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/me/permissions");
      if (error) throw error;
      if (!data) throw new Error("me/permissions returned neither data nor error");
      return data;
    },
    enabled,
    // 5-minute stale window: role changes propagate on next window focus (React Query default).
    // A user demoted mid-session may still click gated actions for up to 5 minutes; a 403 from
    // the mutation API surfaces a toast via the existing problem-details handler (spec §8.4).
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

  const set = useMemo(
    () => new Set(query.data?.permissions ?? []),
    [query.data?.permissions]
  );

  const teamIds = useMemo(
    () => (query.data?.teamMemberships ?? []).map((m) => m.teamId),
    [query.data?.teamMemberships]
  );

  const teamAdminTeamIds = useMemo(
    () =>
      (query.data?.teamMemberships ?? [])
        .filter((m) => m.role === "Admin")
        .map((m) => m.teamId),
    [query.data?.teamMemberships]
  );

  return {
    role: query.data?.role ?? null,
    hasPermission: (perm) => set.has(perm),
    isLoading: enabled && query.isLoading,
    isError: query.isError,
    teamIds,
    teamAdminTeamIds,
  };
}
