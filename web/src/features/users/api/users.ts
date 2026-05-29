import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

type UserDetailResponse = components["schemas"]["UserDetailResponse"];
type UserSummaryResponse = components["schemas"]["UserSummaryResponse"];

export const userKeys = {
  all: ["users"] as const,
  detail: (id: string) => [...userKeys.all, "detail", id] as const,
  search: (q: string, limit: number) => [...userKeys.all, "search", q, limit] as const,
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

/**
 * GET /api/v1/organizations/users?q={query}&limit={n} — server-side substring
 * search by email or displayName (slice-9 spec §6.8). Returns a flat
 * `UserSummaryResponse[]` (NOT cursor-paginated — the endpoint caps at
 * `limit`, default 10).
 *
 * The caller is responsible for debouncing `q` upstream (typically 250 ms in
 * the combobox). The hook respects `enabled` so the caller can gate by
 * `q.length >= 2` without duplicating that branch here.
 *
 * `staleTime: 30 s` reflects the typeahead use-case — the same query is
 * likely re-issued as the user navigates between rows, but the underlying
 * user list rarely changes within a single picker session.
 */
export function useUserSearch(
  q: string,
  options: { limit?: number; enabled?: boolean } = {},
) {
  const limit = options.limit ?? 10;
  const enabled = options.enabled ?? q.length > 0;
  return useQuery({
    queryKey: userKeys.search(q, limit),
    enabled,
    queryFn: async ({ signal }) => {
      const { data, error, response } = await apiClient.GET(
        "/api/v1/organizations/users",
        { params: { query: { q, limit } }, signal },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    staleTime: 30 * 1000,
  });
}

export type { UserDetailResponse, UserSummaryResponse };
