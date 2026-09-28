import { useQuery } from "@tanstack/react-query";
import type { components } from "@/generated/openapi";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import { adminApiClient } from "./client";

export type AdminSession = components["schemas"]["AdminMeResponse"];

export const adminSessionKey = ["admin", "session", "me"] as const;

/** HTTP status attached by `throwWithStatus`; `undefined` for network failures (fetch rejected). */
export function statusOf(error: unknown): number | undefined {
  const s = (error as { __status?: unknown } | null | undefined)?.__status;
  return typeof s === "number" ? s : undefined;
}

/**
 * GET /api/v1/admin/session/me — the access check for the whole console (ADR-0118).
 * 401/403 are terminal answers (re-auth / no access), so they are never retried.
 */
export function useAdminSession() {
  return useQuery<AdminSession>({
    queryKey: adminSessionKey,
    queryFn: async ({ signal }) => {
      const { data, error, response } = await adminApiClient.GET("/api/v1/admin/session/me", { signal });
      if (error) throwWithStatus(error, response);
      return unwrapData(data, response);
    },
    retry: (failureCount, error) => {
      const status = statusOf(error);
      return status !== 401 && status !== 403 && failureCount < 1;
    },
    staleTime: 5 * 60_000,
  });
}
