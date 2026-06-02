import { Link, useParams } from "react-router-dom";
import { Card, CardContent } from "@/components/base/card/card";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useUser } from "@/features/users/api/users";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { usePermissions } from "@/shared/auth/usePermissions";

/**
 * `/users/:id` — single-user detail (slice-9 spec §6.8.2). Three cards:
 *
 *   1. Profile: displayName / email / given+family / createdAt / lastSeenAt
 *   2. Team memberships (from `UserDetailResponse.teams` — no extra fetch)
 *   3. Owned applications (independent `useApplicationsList({ ownerUserId })`)
 *
 * The third card is its own query so a transient apps error doesn't blank
 * out the user profile card — and so the profile is visible while the apps
 * list is still loading.
 *
 * 404 from the user fetch surfaces a distinct "User not found" card so we
 * don't conflate "this user is in another tenant" with "the API is down".
 */
export function UserDetailPage() {
  const { id = "" } = useParams<{ id: string }>();

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRead = !permissionsLoading && hasPermission(KartovaPermissions.OrgUsersRead);

  const userQuery = useUser(canRead ? id : null);
  const appsList = useApplicationsList({
    sortBy: "createdAt",
    sortOrder: "desc",
    ownerUserId: canRead && id ? id : undefined,
  });

  // ----- 403 placeholder ---------------------------------------------------
  if (!permissionsLoading && !canRead) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-primary">Not authorized</p>
          <p className="text-sm text-tertiary">
            You don&apos;t have permission to view users.
          </p>
        </CardContent>
      </Card>
    );
  }

  if (userQuery.isLoading) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="p-6 text-center text-sm text-tertiary">Loading…</CardContent>
      </Card>
    );
  }

  // 404 — cross-tenant or never existed. Distinct copy from "Failed to load".
  const userStatus = (userQuery.error as { __status?: number } | null)?.__status;
  if (userQuery.isError && userStatus === 404) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-3 p-6 text-center">
          <p className="text-base font-medium text-error-primary">User not found</p>
          <p className="text-sm text-tertiary">
            The user may have been removed or doesn&apos;t exist in this tenant.
          </p>
          <Link to="/settings/organization" className="text-sm text-primary hover:underline">
            Back to organization
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (userQuery.isError || !userQuery.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-3 p-6 text-center">
          <p className="text-base font-medium text-error-primary">Failed to load user</p>
          <p className="text-sm text-tertiary">Try refreshing the page.</p>
          <button
            type="button"
            onClick={() => userQuery.refetch()}
            className="text-sm text-primary hover:underline"
          >
            Try again
          </button>
        </CardContent>
      </Card>
    );
  }

  const user = userQuery.data;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-semibold text-primary">{user.displayName}</h2>
        <p className="mt-1 text-sm text-tertiary">{user.email}</p>
      </div>

      {/* Card 1: profile -------------------------------------------------- */}
      <Card>
        <CardContent className="space-y-3">
          <h3 className="text-lg font-semibold text-primary">Profile</h3>
          <dl className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-tertiary">Display name</dt>
              <dd className="text-primary">{user.displayName}</dd>
            </div>
            <div>
              <dt className="text-tertiary">Email</dt>
              <dd className="text-primary">{user.email}</dd>
            </div>
            <div>
              <dt className="text-tertiary">Given name</dt>
              <dd className="text-primary">
                {user.givenName ?? <span className="text-tertiary italic">—</span>}
              </dd>
            </div>
            <div>
              <dt className="text-tertiary">Family name</dt>
              <dd className="text-primary">
                {user.familyName ?? <span className="text-tertiary italic">—</span>}
              </dd>
            </div>
            <div>
              <dt className="text-tertiary">Created</dt>
              <dd className="text-primary">
                {new Date(user.createdAt).toLocaleDateString()}
              </dd>
            </div>
            <div>
              <dt className="text-tertiary">Last seen</dt>
              <dd className="text-primary">
                {user.lastSeenAt
                  ? new Date(user.lastSeenAt).toLocaleDateString()
                  : <span className="text-tertiary italic">Never</span>}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>

      {/* Card 2: team memberships ---------------------------------------- */}
      <Card>
        <CardContent className="space-y-3">
          <h3 className="text-lg font-semibold text-primary">Team memberships</h3>
          {user.teams.length === 0 ? (
            <p className="text-sm text-tertiary">Not on any teams.</p>
          ) : (
            <ul className="divide-y divide-secondary">
              {user.teams.map((t) => (
                <li key={t.teamId} className="flex items-center justify-between py-2">
                  <Link
                    to={`/teams/${t.teamId}`}
                    className="text-sm font-medium text-primary hover:underline"
                  >
                    {t.teamName}
                  </Link>
                  <span className="text-xs uppercase tracking-wide text-tertiary">
                    {t.role}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {/* Card 3: owned applications -------------------------------------- */}
      <Card>
        <CardContent className="space-y-3">
          <h3 className="text-lg font-semibold text-primary">Owned applications</h3>
          {appsList.isLoading ? (
            <p className="text-sm text-tertiary">Loading…</p>
          ) : appsList.isError ? (
            <div className="space-y-2">
              <p className="text-sm text-error-primary">Failed to load applications.</p>
              <button
                type="button"
                onClick={() => appsList.refetch()}
                className="text-sm text-primary hover:underline"
              >
                Try again
              </button>
            </div>
          ) : appsList.items.length === 0 ? (
            <p className="text-sm text-tertiary">Owns no applications.</p>
          ) : (
            <ul className="divide-y divide-secondary">
              {appsList.items.map((app) => (
                <li key={app.id} className="flex items-center justify-between py-2">
                  <Link
                    to={`/catalog/applications/${app.id}`}
                    className="text-sm font-medium text-primary hover:underline"
                  >
                    {app.displayName}
                  </Link>
                  <span className="text-xs uppercase tracking-wide text-tertiary">
                    {app.lifecycle}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

export default UserDetailPage;
