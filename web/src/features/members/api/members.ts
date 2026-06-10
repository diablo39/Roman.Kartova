import { useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import { useCursorList } from "@/lib/list/useCursorList";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type MemberSummaryResponse = components["schemas"]["MemberSummaryResponse"];
type ListMembersQuery = NonNullable<operations["ListMembers"]["parameters"]["query"]>;

// Derive sort-param types from the generated operation so the wire allowlist
// is a single source of truth — backend field additions flow through codegen
// without requiring a hand-edit here (ADR-0095 §4.1).
type MembersListParams = {
  sortBy: NonNullable<ListMembersQuery["sortBy"]>;
  sortOrder: NonNullable<ListMembersQuery["sortOrder"]>;
  role?: string;
  q?: string;
  limit?: number;
};

export const memberKeys = {
  all: ["members"] as const,
  list: (p?: MembersListParams) =>
    p
      ? ([...memberKeys.all, "list", p] as const)
      : ([...memberKeys.all, "list"] as const),
};

export function useMembersList(params: MembersListParams) {
  return useCursorList<MemberSummaryResponse>({
    queryKey: memberKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/users", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            role: params.role,
            q: params.q,
            // The ListMembers endpoint serialises `limit` as a string query
            // param (unlike ListTeams which uses a number), so the String(...)
            // cast is required here — not a stylistic convention.
            limit: params.limit !== undefined ? String(params.limit) : "50",
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useChangeMemberRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { userId: string; role: string }) => {
      const { error, response } = await apiClient.PUT(
        "/api/v1/organizations/users/{id}/role",
        {
          params: { path: { id: input.userId } },
          body: { role: input.role },
        },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: memberKeys.all }),
  });
}

export function useOffboardMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { userId: string; successorUserId: string }) => {
      const { error, response } = await apiClient.DELETE(
        "/api/v1/organizations/users/{id}",
        {
          params: { path: { id: input.userId } },
          body: { successorUserId: input.successorUserId },
        },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: memberKeys.all }),
  });
}

export type { MemberSummaryResponse };
