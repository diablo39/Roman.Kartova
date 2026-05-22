import { useAuth } from "react-oidc-context";
import { useQuery } from "@tanstack/react-query";

import type { KartovaPermission } from "./permissions";

interface MePermissionsResponse {
  role: string;
  permissions: readonly string[];
}

const QUERY_KEY = ["me", "permissions"] as const;

export interface UsePermissionsResult {
  role: string | null;
  hasPermission: (perm: KartovaPermission) => boolean;
  isLoading: boolean;
}

// TODO(slice-7): migrate to `apiClient.GET("/api/v1/organizations/me/permissions")` once the
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
      if (!res.ok) throw new Error(`me/permissions returned ${res.status}`);
      return (await res.json()) as MePermissionsResponse;
    },
    enabled,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

  const set = new Set(query.data?.permissions ?? []);

  return {
    role: query.data?.role ?? null,
    hasPermission: (perm) => set.has(perm),
    isLoading: enabled && query.isLoading,
  };
}
