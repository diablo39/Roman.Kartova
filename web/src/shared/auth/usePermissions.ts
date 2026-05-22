import { useMemo } from "react";
import { useAuth } from "react-oidc-context";
import { useQuery } from "@tanstack/react-query";

import type { KartovaPermission } from "./permissions";

interface MePermissionsResponse {
  role: string | null;
  permissions: readonly string[];
}

const QUERY_KEY = ["me", "permissions"] as const;

export interface UsePermissionsResult {
  role: string | null;
  hasPermission: (perm: KartovaPermission) => boolean;
  isLoading: boolean;
  isError: boolean;
}

// TODO(api-codegen): migrate to `apiClient.GET("/api/v1/organizations/me/permissions")` once the
// Docker-running API container is rebuilt with slice-7 changes and `pnpm codegen` picks up
// the new endpoint. For now we use raw fetch so this hook can land without depending on
// codegen ordering.
export function usePermissions(): UsePermissionsResult {
  const auth = useAuth();
  const enabled = auth.isAuthenticated;

  const query = useQuery<MePermissionsResponse>({
    queryKey: QUERY_KEY,
    queryFn: async () => {
      const headers: Record<string, string> = { Accept: "application/json" };
      if (auth.user?.access_token) {
        headers.Authorization = `Bearer ${auth.user.access_token}`;
      }
      const res = await fetch("/api/v1/organizations/me/permissions", { headers });
      if (!res.ok) {
        const err = Object.assign(
          new Error(`me/permissions returned ${res.status}`),
          { __status: res.status }
        );
        throw err;
      }
      return (await res.json()) as MePermissionsResponse;
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

  return {
    role: query.data?.role ?? null,
    hasPermission: (perm) => set.has(perm),
    isLoading: enabled && query.isLoading,
    isError: query.isError,
  };
}
