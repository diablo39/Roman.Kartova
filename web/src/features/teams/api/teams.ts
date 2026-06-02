import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import { useCursorList } from "@/lib/list/useCursorList";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type TeamResponse = components["schemas"]["TeamResponse"];
type TeamDetailResponse = components["schemas"]["TeamDetailResponse"];
type TeamMemberResponse = components["schemas"]["TeamMemberResponse"];
type CreateTeamRequest = components["schemas"]["CreateTeamRequest"];
type UpdateTeamRequest = components["schemas"]["UpdateTeamRequest"];
type AddTeamMemberRequest = components["schemas"]["AddTeamMemberRequest"];
type UpdateTeamMemberRequest = components["schemas"]["UpdateTeamMemberRequest"];

// Derive sort-param types from the generated OpenAPI operation so the wire
// allowlist is a single source of truth — adding a new sort field on the
// backend (ADR-0095 §4.1) flows through codegen without requiring a hand
// edit here.
type ListTeamsQuery = NonNullable<operations["ListTeams"]["parameters"]["query"]>;

type TeamsListParams = {
  sortBy: NonNullable<ListTeamsQuery["sortBy"]>;
  sortOrder: NonNullable<ListTeamsQuery["sortOrder"]>;
  limit?: number;
};

export const teamKeys = {
  all: ["teams"] as const,
  list: (params?: TeamsListParams) =>
    params
      ? ([...teamKeys.all, "list", params] as const)
      : ([...teamKeys.all, "list"] as const),
  detail: (id: string) => [...teamKeys.all, "detail", id] as const,
};

export function useTeamsList(params: TeamsListParams) {
  return useCursorList<TeamResponse>({
    queryKey: teamKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/teams", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useTeam(id: string) {
  return useQuery({
    queryKey: teamKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET(
        "/api/v1/organizations/teams/{id}",
        { params: { path: { id } } },
      );
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useCreateTeam() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: CreateTeamRequest) => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/organizations/teams",
        { body: input },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      // Invalidate the list prefix — covers all parameterized queryKeys.
      qc.invalidateQueries({ queryKey: teamKeys.all });
    },
  });
}

export function useUpdateTeam(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateTeamRequest) => {
      const { data, error, response } = await apiClient.PUT(
        "/api/v1/organizations/teams/{id}",
        { params: { path: { id } }, body: input },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: (data) => {
      // Update the cached detail with the fresh response and refetch lists.
      qc.setQueryData(teamKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: teamKeys.list() });
    },
  });
}

export function useDeleteTeam(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await apiClient.DELETE(
        "/api/v1/organizations/teams/{id}",
        { params: { path: { id } } },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: teamKeys.all });
    },
  });
}

export function useAddTeamMember(teamId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddTeamMemberRequest) => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/organizations/teams/{id}/members",
        { params: { path: { id: teamId } }, body: input },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
    },
  });
}

export function useRemoveTeamMember(teamId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (userId: string) => {
      const { error, response } = await apiClient.DELETE(
        "/api/v1/organizations/teams/{id}/members/{userId}",
        { params: { path: { id: teamId, userId } } },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
    },
  });
}

export function useChangeTeamMemberRole(teamId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { userId: string; role: UpdateTeamMemberRequest["role"] }) => {
      const { error, response } = await apiClient.PUT(
        "/api/v1/organizations/teams/{id}/members/{userId}",
        {
          params: { path: { id: teamId, userId: input.userId } },
          body: { role: input.role },
        },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: teamKeys.detail(teamId) });
    },
  });
}

export type { TeamResponse, TeamDetailResponse, TeamMemberResponse };
