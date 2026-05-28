import { useMutation } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import type { components } from "@/generated/openapi";

type SessionStartResponse = components["schemas"]["SessionStartResponse"];

/**
 * Re-throws an openapi-fetch error after attaching the HTTP status as a
 * `__status` field so callers can branch on auth failures (401/403/502)
 * without re-parsing the response. Mirrors the helper in
 * `organization/api/organization.ts` and friends.
 */
function throwWithStatus(error: unknown, response: { status: number }): never {
  (error as Record<string, unknown>).__status = response.status;
  throw error;
}

function unwrapData<T>(data: T | undefined): T {
  if (!data) throw new Error("API returned neither data nor error");
  return data;
}

/**
 * POST /api/v1/auth/session — exchanges the OIDC token for a Kartova session
 * (slice-9 spec §6). On success the response carries the user's profile,
 * role, permissions, team memberships, the organization snapshot, and the
 * `acceptedInvitation` payload (non-null when the OIDC callback auto-accepted
 * an outstanding invitation in the same hop). Callers should prepopulate
 * `orgKeys.profile()` from the response so the welcome screen and shell
 * skip a second roundtrip.
 */
export function useStartSession() {
  return useMutation({
    mutationFn: async () => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/auth/session",
        {},
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
  });
}

export type { SessionStartResponse };
