import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useMembersList } from "@/features/members/api/members";
import { ChangeMemberRoleDialog } from "@/features/members/components/ChangeMemberRoleDialog";
import { OffboardMemberConfirmDialog } from "@/features/members/components/OffboardMemberConfirmDialog";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["displayName", "role", "createdAt"] as const;
const TEXT_FILTERS = ["role", "q"] as const;
const FILTER_SPECS: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
      { label: "Member", value: "Member" },
      { label: "OrgAdmin", value: "OrgAdmin" },
    ],
  },
  { key: "q", type: "text", label: "Search members", placeholder: "Search by name or email…" },
];

export function MembersListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useMembersList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    role: filters.textValues.role,
    q: filters.textValues.q,
  });

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canChangeRole = !permissionsLoading && hasPermission(KartovaPermissions.OrgUsersRoleChange);
  const canRemove = !permissionsLoading && hasPermission(KartovaPermissions.OrgUsersRemove);

  const [roleTarget, setRoleTarget] = useState<{ userId: string; role: string } | null>(null);
  const [offboardTarget, setOffboardTarget] = useState<{ userId: string; displayName: string } | null>(null);

  useEffect(() => {
    if (list.isError) {
      console.error("MembersListPage list error", list.error);
    }
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Members</h2>
      </div>

      <FilterBar specs={FILTER_SPECS} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load members</p>
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
            <p className="text-base font-medium text-primary">
              {filters.isActive ? "No members match your filters" : "No members yet"}
            </p>
            <p className="text-sm text-tertiary">
              {filters.isActive ? "Try a different role or search." : "Invite users to add members."}
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
          <table className="w-full text-left text-sm">
            <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
              <tr>
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Email</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Teams</th>
                <th className="px-4 py-3 font-medium">Last seen</th>
                {(canChangeRole || canRemove) && <th className="px-4 py-3" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-secondary">
              {list.items.map(m => (
                <tr key={m.id} className="hover:bg-primary_hover">
                  <td className="px-4 py-3">
                    <Link to={`/users/${m.id}`} className="font-medium text-primary hover:underline">
                      {m.displayName}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-tertiary">{m.email}</td>
                  <td className="px-4 py-3 text-primary">{m.role}</td>
                  <td className="px-4 py-3 text-tertiary">{m.teamCount}</td>
                  <td className="px-4 py-3 text-tertiary">
                    {m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleDateString() : "—"}
                  </td>
                  {(canChangeRole || canRemove) && (
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        {canChangeRole && (
                          <Button size="sm" color="secondary" onClick={() => setRoleTarget({ userId: m.id, role: m.role })}>
                            Change role
                          </Button>
                        )}
                        {canRemove && (
                          <Button size="sm" color="secondary" onClick={() => setOffboardTarget({ userId: m.id, displayName: m.displayName })}>
                            Remove
                          </Button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ChangeMemberRoleDialog
        userId={roleTarget?.userId ?? ""}
        currentRole={roleTarget?.role ?? "Member"}
        open={roleTarget !== null}
        onOpenChange={(open) => { if (!open) setRoleTarget(null); }}
      />
      <OffboardMemberConfirmDialog
        userId={offboardTarget?.userId ?? ""}
        displayName={offboardTarget?.displayName ?? ""}
        open={offboardTarget !== null}
        onOpenChange={(open) => { if (!open) setOffboardTarget(null); }}
      />
    </div>
  );
}
