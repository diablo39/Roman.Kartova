import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import type { components } from "@/generated/openapi";

type UserDetailResponse = components["schemas"]["UserDetailResponse"];

/**
 * Re-throws an openapi-fetch error after attaching the HTTP status as a
 * `__status` field. Kept here (rather than imported from another feature) so
 * `users/` is self-contained and F5 can extend without cross-feature coupling.
 */
function throwWithStatus(error: unknown, response: { status: number }): never {
  (error as Record<string, unknown>).__status = response.status;
  throw error;
}

function unwrapData<T>(data: T | undefined): T {
  if (!data) throw new Error("API returned neither data nor error");
  return data;
}

export const userKeys = {
  all: ["users"] as const,
  detail: (id: string) => [...userKeys.all, "detail", id] as const,
};

/**
 * GET /api/v1/organizations/users/{id} — fetches a single user's detail (id,
 * email, displayName, given/family names, team memberships, timestamps).
 *
 * Designed as the minimal F4 dependency so the InvitationsPage can resolve
 * `invitedByUserId` → display name without rendering a raw UUID in the
 * interim. F5 will extend this file with `useUserSearch` for the "Add user"
 * combobox.
 *
 * The hook respects `id == null | undefined | ""` by setting `enabled: false`,
 * so callers can render `useUser(invitedByUserId)` unconditionally even when
 * the parent row data is still loading.
 *
 * `staleTime: 5 min` reflects the data shape: user display fields change
 * rarely, and the same user typically appears on many rows of the page.
 */
export function useUser(id: string | undefined | null) {
  return useQuery({
    queryKey: userKeys.detail(id ?? ""),
    enabled: !!id,
    queryFn: async ({ signal }) => {
      const { data, error, response } = await apiClient.GET(
        "/api/v1/organizations/users/{id}",
        { params: { path: { id: id! } }, signal },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    staleTime: 5 * 60 * 1000,
  });
}

export type { UserDetailResponse };
