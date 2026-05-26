import { Link, useParams } from "react-router-dom";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useTeam } from "@/features/teams/api/teams";
import { usePermissions } from "@/shared/auth/usePermissions";

export function TeamDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const teamQuery = useTeam(id);
  const { role, teamAdminTeamIds } = usePermissions();

  const isAdmin = role === "OrgAdmin" || teamAdminTeamIds.includes(id);

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
            {/* TODO Task 30: wire RenameTeamDialog */}
            <Button size="sm" color="secondary">Rename</Button>
            {/* TODO Task 30: wire DeleteTeamDialog */}
            <Button size="sm" color="primary-destructive">Delete</Button>
          </div>
        )}
      </div>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-primary">Members</h3>
          {isAdmin && (
            // TODO Task 30: wire AddTeamMemberDialog
            <Button size="sm" color="secondary">Add member</Button>
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
                    <td className="px-4 py-3 font-mono text-xs text-tertiary">{m.userId}</td>
                    <td className="px-4 py-3 text-primary">{m.role}</td>
                    <td className="px-4 py-3 text-tertiary">
                      {new Date(m.addedAt).toLocaleDateString()}
                    </td>
                    {isAdmin && (
                      <td className="px-4 py-3 text-right">
                        {/* TODO Task 30: wire RemoveMemberDialog */}
                        <Button size="sm" color="secondary">Remove</Button>
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
        {team.applicationIds.length === 0 ? (
          <p className="text-sm text-tertiary">No applications linked to this team.</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {team.applicationIds.map(appId => (
              <li key={appId}>
                <Link
                  to={`/catalog/applications/${appId}`}
                  className="font-mono text-xs text-primary hover:underline"
                >
                  {appId}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
