import { useEffect, useState } from "react";
import { Plus } from "@untitledui/icons";

import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { Badge } from "@/components/base/badges/badges";
import type { BadgeColors } from "@/components/base/badges/badge-types";

import {
  useInvitationsList,
  type InvitationResponse,
  type InvitationStatus,
} from "@/features/organization/api/invitations";
import { InviteUserDialog } from "@/features/organization/components/InviteUserDialog";
import { RevokeInvitationConfirm } from "@/features/organization/components/RevokeInvitationConfirm";
import { useUser } from "@/features/users/api/users";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

/**
 * Sort fields the C# `ListInvitations` handler accepts (slice-9 spec §6.7).
 * The generated OpenAPI types widen these to `string` because the binding is
 * `[FromQuery] string?`, so we narrow here as the single source of truth used
 * by both the page and the URL-state hook.
 */
const ALLOWED_SORT_FIELDS = ["invitedAt", "expiresAt", "email"] as const;

const STATUS_TO_BADGE_COLOR: Record<string, BadgeColors> = {
  Pending: "blue",
  Accepted: "success",
  Revoked: "gray",
  Expired: "error",
};

/**
 * Pure formatter for the "Expires in …" column. Takes the remaining milliseconds
 * (computed at the call site so the page-level interval can drive re-renders)
 * and renders a human-friendly relative phrase. Exported indirectly via the
 * component closure — kept inline so the unit boundary stays at the page level.
 */
function formatExpiry(ms: number): string {
  if (ms <= 0) return "Expired";
  if (ms < 3_600_000) return `in ${Math.ceil(ms / 60_000)} min`;
  if (ms < 86_400_000) return `in ${Math.ceil(ms / 3_600_000)} hr`;
  return `in ${Math.ceil(ms / 86_400_000)} days`;
}

/**
 * Sub-component for the "Invited by" cell. Resolves `invitedByUserId` to a
 * display name via the minimal F4 `useUser` shim. Falls back to a muted UUID
 * if the lookup errors (e.g. the inviter has since been deleted from the
 * tenant) — the row is still useful, the inviter just renders as an id.
 */
function InvitedByCell({ userId }: { userId: string }) {
  const userQuery = useUser(userId);
  if (userQuery.isLoading) return <span className="text-tertiary">…</span>;
  if (userQuery.isError || !userQuery.data) {
    return <span className="font-mono text-xs text-tertiary">{userId}</span>;
  }
  return <span>{userQuery.data.displayName || userQuery.data.email}</span>;
}

/**
 * `/settings/organization/invitations` — list page for pending and historical
 * invitations (slice-9 spec §6.7). Route wiring lands in F7.
 *
 * Three permission gates layered top-down:
 *   - `org.invitations.read` → page (read-side; missing it shows a 403 card).
 *   - `org.invitations.create` → "Invite user" button.
 *   - `org.invitations.revoke` → per-row Revoke button (also requires the
 *     row's `status === "Pending"`).
 *
 * Tab strip:
 *   - "Pending" (default) → server filters to `status=Pending`.
 *   - "All" → no status filter; lifecycle badge identifies each row.
 *
 * Live countdown:
 *   - A page-level `setInterval(60_000)` bumps a `now` state every minute so
 *     the "Expires in …" cell stays accurate without polling the server.
 *     `formatExpiry` is pure — the interval is the only async piece.
 */
export function InvitationsPage() {
  const { sortBy, sortOrder } = useListUrlState({
    defaultSortBy: "invitedAt",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });

  const [activeTab, setActiveTab] = useState<"Pending" | "All">("Pending");

  // Server-side default is now `Pending` (spec §6.7) — the "All" tab MUST
  // pass the explicit `"all"` sentinel to opt out of the filter, otherwise
  // an omitted `status` would silently land back on the Pending default and
  // the All tab would mirror the Pending tab.
  const list = useInvitationsList({
    sortBy,
    sortOrder,
    status: activeTab === "Pending" ? ("Pending" satisfies InvitationStatus) : "all",
  });

  const [dialogOpen, setDialogOpen] = useState(false);
  const [revokingInvitation, setRevokingInvitation] = useState<InvitationResponse | null>(null);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRead = !permissionsLoading && hasPermission(KartovaPermissions.OrgInvitationsRead);
  const canCreate = !permissionsLoading && hasPermission(KartovaPermissions.OrgInvitationsCreate);
  const canRevoke = !permissionsLoading && hasPermission(KartovaPermissions.OrgInvitationsRevoke);

  // Live "now" — drives the per-row expiry countdown. Cleared on unmount so we
  // never setState on an unmounted component.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    if (list.isError) {
      console.error("InvitationsPage list error", list.error);
    }
  }, [list.isError, list.error]);

  // ----- 403 placeholder ---------------------------------------------------
  if (!permissionsLoading && !canRead) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-primary">Not authorized</p>
          <p className="text-sm text-tertiary">
            You don&apos;t have permission to view invitations.
          </p>
        </CardContent>
      </Card>
    );
  }

  const onTabChange = (next: "Pending" | "All") => {
    if (next === activeTab) return;
    setActiveTab(next);
    // Drop cursor stack so the new tab starts from page one. The queryKey
    // change alone would trigger a fresh fetch, but `reset()` also wipes the
    // Prev/Next stack so the user can't paginate against the previous filter.
    list.reset();
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Invitations</h2>
        {canCreate && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Invite user
          </Button>
        )}
      </div>

      {/* Tab strip — native buttons, underline + color treatment on active. */}
      <div className="flex items-center gap-2 border-b border-secondary">
        {(["Pending", "All"] as const).map((tab) => {
          const active = tab === activeTab;
          return (
            <button
              key={tab}
              type="button"
              onClick={() => onTabChange(tab)}
              className={
                active
                  ? "border-b-2 border-brand-500 px-4 py-2 text-sm font-medium text-primary"
                  : "border-b-2 border-transparent px-4 py-2 text-sm font-medium text-tertiary hover:text-secondary"
              }
              aria-current={active ? "page" : undefined}
            >
              {tab}
            </button>
          );
        })}
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load invitations</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : list.isLoading ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="p-6 text-center text-sm text-tertiary">Loading…</CardContent>
        </Card>
      ) : list.items.length === 0 ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No invitations</p>
            <p className="text-sm text-tertiary">
              {activeTab === "Pending"
                ? "No pending invitations. Click Invite user to send one."
                : "No invitations have been sent yet."}
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
          <table className="w-full text-left text-sm">
            <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
              <tr>
                <th className="px-4 py-3 font-medium">Email</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Invited by</th>
                <th className="px-4 py-3 font-medium">Invited at</th>
                <th className="px-4 py-3 font-medium">Expires</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-secondary">
              {list.items.map((invitation) => {
                const expiryMs = new Date(invitation.expiresAt).getTime() - now;
                const badgeColor = STATUS_TO_BADGE_COLOR[invitation.status] ?? "gray";
                const canRevokeRow = canRevoke && invitation.status === "Pending";

                return (
                  <tr key={invitation.id} className="hover:bg-primary_hover">
                    <td className="px-4 py-3 text-primary">{invitation.email}</td>
                    <td className="px-4 py-3 text-tertiary">{invitation.role}</td>
                    <td className="px-4 py-3 text-tertiary">
                      <InvitedByCell userId={invitation.invitedByUserId} />
                    </td>
                    <td className="px-4 py-3 text-tertiary">
                      {new Date(invitation.invitedAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3 text-tertiary">{formatExpiry(expiryMs)}</td>
                    <td className="px-4 py-3">
                      <Badge color={badgeColor} size="sm">{invitation.status}</Badge>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {canRevokeRow && (
                        <Button
                          size="sm"
                          color="secondary"
                          onClick={() => setRevokingInvitation(invitation)}
                        >
                          Revoke
                        </Button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <InviteUserDialog open={dialogOpen} onOpenChange={setDialogOpen} />
      <RevokeInvitationConfirm
        invitation={revokingInvitation}
        open={revokingInvitation !== null}
        onOpenChange={(open) => {
          if (!open) setRevokingInvitation(null);
        }}
      />
    </div>
  );
}

export default InvitationsPage;
