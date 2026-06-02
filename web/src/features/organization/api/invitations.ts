import { useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import { useCursorList } from "@/lib/list/useCursorList";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type InvitationResponse = components["schemas"]["InvitationResponse"];
type CreateInvitationRequest = components["schemas"]["CreateInvitationRequest"];
type CreateInvitationResponse = components["schemas"]["CreateInvitationResponse"];

// The handler accepts case-insensitive sort-field / sort-order names
// ("invitedAt"|"expiresAt"|"email" / "asc"|"desc"). The generated OpenAPI
// types currently widen these to `string` because the C# binding is the
// permissive `[FromQuery] string?`, so we expose a narrowed shape via the
// `InvitationSortField` / `InvitationSortOrder` aliases — single source of
// truth for the page and tests.
type ListInvitationsQuery = NonNullable<
  operations["ListInvitations"]["parameters"]["query"]
>;

/**
 * Lifecycle states for an invitations row. The wire enum (spec §6.7) carries
 * exactly these four — `"all"` is a query-string sentinel on the list
 * endpoint, NOT a status an individual invitation can be in, so it lives on
 * {@link InvitationsListParams} below rather than this union.
 */
export type InvitationStatus = "Pending" | "Accepted" | "Revoked" | "Expired";

/**
 * Server-side filter for the invitations list endpoint. Mirrors the spec §6.7
 * grammar `status ∈ {pending, accepted, revoked, expired, all}` exactly:
 *   - one of the four lifecycle states → server filters to that state
 *   - `"all"` → opt out of the filter (legacy `undefined` would now default to Pending)
 * Defaults to Pending server-side when the query string omits the value;
 * callers wanting the whole list MUST pass `"all"` explicitly.
 */
export type InvitationsListStatusFilter = InvitationStatus | "all";

export interface InvitationsListParams {
  sortBy: NonNullable<ListInvitationsQuery["sortBy"]>;
  sortOrder: NonNullable<ListInvitationsQuery["sortOrder"]>;
  limit?: number;
  /**
   * When set, server filters to only invitations in this status — or, for
   * the <c>"all"</c> sentinel, opts out of the default Pending filter.
   * Omit the field to fall back to the server-side default (Pending).
   */
  status?: InvitationsListStatusFilter;
}

export const invitationKeys = {
  all: ["invitations"] as const,
  list: (params?: InvitationsListParams) =>
    params
      ? ([...invitationKeys.all, "list", params] as const)
      : ([...invitationKeys.all, "list"] as const),
};

/**
 * GET /api/v1/organizations/invitations — cursor-paginated list of invitations
 * visible to the current tenant (slice-9 spec §6.7). Optional `status` filter
 * narrows to a single lifecycle state; omit to list all four.
 *
 * The wire `limit` query is typed as `string` in the generated OpenAPI
 * (`[FromQuery] string?` on the C# binding); we stringify the SPA's
 * `number` here so callers can keep using the familiar number type.
 */
export function useInvitationsList(params: InvitationsListParams) {
  return useCursorList<InvitationResponse>({
    queryKey: invitationKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET(
        "/api/v1/organizations/invitations",
        {
          params: {
            query: {
              sortBy: params.sortBy,
              sortOrder: params.sortOrder,
              limit: String(params.limit ?? 50),
              cursor,
              status: params.status,
            },
          },
        },
      );
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

/**
 * POST /api/v1/organizations/invitations — creates a new invitation.
 * Returns the full <c>CreateInvitationResponse</c> with the new invitation
 * plus the copy-link `inviteUrl` (slice-9 spec §6.7).
 *
 * Failure surface:
 *  - 409 with `type=email-already-in-tenant`     — email already a tenant member.
 *  - 409 with `type=email-already-invited`        — pending invite already exists.
 *  - 409 with `type=email-already-on-platform`    — KeyCloak user exists outside tenant.
 *  - 422 — basic validation rejected by server beyond client's reach.
 *  - 502 — upstream KeyCloak failure.
 *
 * Each path re-throws with `__status` attached and the raw ProblemDetails so
 * the dialog can branch on `problem.type` to surface a friendly toast.
 */
export function useCreateInvitation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: CreateInvitationRequest) => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/organizations/invitations",
        { body: input },
      );
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      // Invalidate the list prefix — covers every parameterized status/sort
      // variant so the Pending tab refetches and includes the new row.
      qc.invalidateQueries({ queryKey: invitationKeys.all });
    },
  });
}

/**
 * POST /api/v1/organizations/invitations/{id}/revoke — revokes a pending
 * invitation (slice-9 spec §6.7). 204 on success.
 *
 * Failure surface:
 *  - 404 — invitation not visible in the current tenant (RLS or unknown id).
 *  - 409 — invitation is not in `Pending` state (already accepted/revoked/expired).
 *  - 502 — upstream KeyCloak failure during the rollback of the pre-created user.
 *
 * Both 404 and 409 are end-states from the user's perspective ("it's gone or
 * it's not pending any more, refresh"). The dialog treats them the same way
 * (toast + close) — the list refetch will surface the current truth.
 */
export function useRevokeInvitation(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await apiClient.POST(
        "/api/v1/organizations/invitations/{id}/revoke",
        { params: { path: { id } } },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: invitationKeys.all });
    },
  });
}

export type {
  InvitationResponse,
  CreateInvitationRequest,
  CreateInvitationResponse,
};
