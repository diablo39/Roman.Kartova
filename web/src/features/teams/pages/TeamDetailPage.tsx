import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useTeam } from "@/features/teams/api/teams";
import { RenameTeamDialog } from "@/features/teams/components/RenameTeamDialog";
import { DeleteTeamConfirmDialog } from "@/features/teams/components/DeleteTeamConfirmDialog";
import { AddMemberDialog } from "@/features/teams/components/AddMemberDialog";
import { RemoveMemberConfirmDialog } from "@/features/teams/components/RemoveMemberConfirmDialog";
import { ChangeRoleDialog } from "@/features/teams/components/ChangeRoleDialog";
import { usePermissions } from "@/shared/auth/usePermissions";

// Dialog wiring overview (Task 30):
//   - Rename / Delete / Add member: simple boolean state
//   - Remove member / Change role: also track which user is being acted on
//
// Still stubbed: AssignTeamPicker on application detail pages — Task 31.

export function TeamDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const teamQuery = useTeam(id);
  const { role, teamAdminTeamIds } = usePermissions();

  const isAdmin = role === "OrgAdmin" || teamAdminTeamIds.includes(id);

  const [renameOpen, setRenameOpen] = useState(false);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [addMemberOpen, setAddMemberOpen] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<string | null>(null);
  const [roleTarget, setRoleTarget] = useState<{ userId: string; role: string } | null>(null);

  if (teamQuery.isLoading) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="p-6 text-center text-sm text-tertiary">Loading…</CardContent>
      </Card>
    );
  }

  if (teamQuery.isError || !teamQuery.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-3 p-6 text-center">
          <p className="text-base font-medium text-error-primary">Failed to load team</p>
          <p className="text-sm text-tertiary">The team may not exist or you may not have access.</p>
          <Link to="/teams" className="text-sm text-primary hover:underline">
            Back to teams
          </Link>
        </CardContent>
      </Card>
    );
  }

  const team = teamQuery.data;

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h2 className="text-2xl font-semibold text-primary">{team.displayName}</h2>
          <p className="mt-1 text-sm text-tertiary">
            {team.description || <span className="italic">No description</span>}
          </p>
        </div>
        {isAdmin && (
          <div className="flex gap-2">
            <Button size="sm" color="secondary" onClick={() => setRenameOpen(true)}>
              Rename
            </Button>
            <Button size="sm" color="primary-destructive" onClick={() => setDeleteOpen(true)}>
              Delete
            </Button>
          </div>
        )}
      </div>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-primary">Members</h3>
          {isAdmin && (
            <Button size="sm" color="secondary" onClick={() => setAddMemberOpen(true)}>
              Add member
            </Button>
          )}
        </div>
        {team.members.length === 0 ? (
          <p className="text-sm text-tertiary">No members yet.</p>
        ) : (
          <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
            <table className="w-full text-left text-sm">
              <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
                <tr>
                  <th className="px-4 py-3 font-medium">User</th>
                  <th className="px-4 py-3 font-medium">Role</th>
                  <th className="px-4 py-3 font-medium">Added</th>
                  {isAdmin && <th className="px-4 py-3" />}
                </tr>
              </thead>
              <tbody className="divide-y divide-secondary">
                {team.members.map(m => (
                  <tr key={m.userId} className="hover:bg-primary_hover">
                    <td className="px-4 py-3">
                      {/* Slice 9 (F8): TeamMemberResponse carries displayName + email
                          via E3 enrichment. Fall back to the bare userId UUID when
                          displayName is empty (spec §4.1 — a freshly added user whose
                          profile hasn't been populated yet). */}
                      <div className="font-medium text-primary">{m.displayName || m.userId}</div>
                      <div className="text-xs text-tertiary">{m.email}</div>
                    </td>
                    <td className="px-4 py-3 text-primary">{m.role}</td>
                    <td className="px-4 py-3 text-tertiary">
                      {new Date(m.addedAt).toLocaleDateString()}
                    </td>
                    {isAdmin && (
                      <td className="px-4 py-3 text-right">
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            color="secondary"
                            onClick={() => setRoleTarget({ userId: m.userId, role: m.role })}
                          >
                            Change role
                          </Button>
                          <Button
                            size="sm"
                            color="secondary"
                            onClick={() => setRemoveTarget(m.userId)}
                          >
                            Remove
                          </Button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="space-y-3">
        <h3 className="text-lg font-semibold text-primary">Applications</h3>
        {team.applications.length === 0 ? (
          <p className="text-sm text-tertiary">No applications linked to this team.</p>
        ) : (
          <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
            <table className="w-full text-left text-sm">
              <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
                <tr>
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Lifecycle</th>
                  <th className="px-4 py-3 font-medium">Id</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-secondary">
                {team.applications.map(app => (
                  <tr key={app.id} className="hover:bg-primary_hover">
                    <td className="px-4 py-3">
                      <Link
                        to={`/catalog/applications/${app.id}`}
                        className="font-medium text-primary hover:underline"
                      >
                        {app.displayName}
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-primary">{app.lifecycle}</td>
                    <td className="px-4 py-3 font-mono text-xs text-tertiary">{app.id}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <RenameTeamDialog team={team} open={renameOpen} onOpenChange={setRenameOpen} />
      <DeleteTeamConfirmDialog team={team} open={deleteOpen} onOpenChange={setDeleteOpen} />
      <AddMemberDialog teamId={team.id} open={addMemberOpen} onOpenChange={setAddMemberOpen} />
      <RemoveMemberConfirmDialog
        teamId={team.id}
        userId={removeTarget ?? ""}
        open={removeTarget !== null}
        onOpenChange={(open) => {
          if (!open) setRemoveTarget(null);
        }}
      />
      <ChangeRoleDialog
        teamId={team.id}
        userId={roleTarget?.userId ?? ""}
        currentRole={roleTarget?.role ?? "Member"}
        open={roleTarget !== null}
        onOpenChange={(open) => {
          if (!open) setRoleTarget(null);
        }}
      />
    </div>
  );
}
