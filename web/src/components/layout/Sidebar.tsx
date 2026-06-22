import { Link, NavLink, useLocation } from "react-router-dom";
import { cx } from "@/lib/utils/cx";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

/**
 * Visual section header inside the sidebar. Renders a small uppercase title
 * above a stack of `NavItemLink`s — used by Slice-9 F7 to group the
 * permission-gated "Settings" sub-navigation under a dedicated heading so it
 * reads as a distinct section instead of a fourth top-level link.
 */
function NavGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="mt-4 space-y-1" data-testid={`nav-group-${title.toLowerCase()}`}>
      <div className="px-3 pt-3 pb-1 text-xs font-medium uppercase tracking-wide text-tertiary">
        {title}
      </div>
      {children}
    </div>
  );
}

const navItemClass = (active: boolean) =>
  cx(
    "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
    active ? "bg-brand-solid text-white" : "text-secondary hover:bg-primary_hover",
  );

/**
 * Active-aware navigation link styled to match the existing top-level entries.
 * Extracted so the Settings group can render sub-items with identical chrome.
 *
 * `activeWhen` overrides react-router's default descendant matching for items
 * whose route is a path-prefix of a *sibling* nav item. The Catalog item
 * (`/catalog`) needs it because Services lives at `/catalog/services`: without
 * the override, NavLink's default matching highlights Catalog on every
 * `/catalog/*` route — including the Services pages — so both light up at once.
 * When supplied we drive both the highlight and `aria-current` from the
 * predicate (a className-only override would leave `aria-current` wrong).
 */
function NavItemLink({
  to,
  label,
  activeWhen,
}: {
  to: string;
  label: string;
  activeWhen?: (pathname: string) => boolean;
}) {
  const { pathname } = useLocation();

  if (activeWhen) {
    const active = activeWhen(pathname);
    return (
      <Link to={to} aria-current={active ? "page" : undefined} className={navItemClass(active)}>
        {label}
      </Link>
    );
  }

  return (
    <NavLink to={to} className={({ isActive }) => navItemClass(isActive)}>
      {label}
    </NavLink>
  );
}

function DisabledItem({ label }: { label: string }) {
  return (
    <span
      className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-tertiary opacity-50"
      data-disabled="true"
    >
      {label}
    </span>
  );
}

export function Sidebar() {
  const { hasPermission } = usePermissions();
  const canSeeTeams = hasPermission(KartovaPermissions.TeamRead);
  const canSeeMembers = hasPermission(KartovaPermissions.OrgUsersRead);
  const canSeeOrgSettings = hasPermission(KartovaPermissions.OrgProfileRead);
  const canSeeInvitations = hasPermission(KartovaPermissions.OrgInvitationsRead);

  return (
    <aside className="flex h-full w-[260px] flex-col border-r border-secondary bg-secondary">
      <div className="flex h-14 items-center border-b border-secondary px-4">
        <span className="text-lg font-semibold text-primary">Kartova</span>
      </div>
      <nav className="flex-1 overflow-y-auto p-3">
        <ul className="space-y-1">
          <li>
            <NavItemLink
              to="/catalog"
              label="Catalog"
              activeWhen={(p) => p === "/catalog" || p.startsWith("/catalog/applications")}
            />
          </li>
          {canSeeTeams && (
            <li>
              <NavItemLink to="/teams" label="Teams" />
            </li>
          )}
          {canSeeMembers && (
            <li>
              <NavItemLink to="/members" label="Members" />
            </li>
          )}
          <li>
            <NavItemLink to="/catalog/services" label="Services" />
          </li>
          <li>
            <DisabledItem label="Infrastructure" />
          </li>
          <li>
            <DisabledItem label="Docs" />
          </li>
        </ul>
        {canSeeOrgSettings && (
          <NavGroup title="Settings">
            <ul className="space-y-1">
              <li>
                <NavItemLink to="/settings/organization" label="Organization" />
              </li>
              {canSeeInvitations && (
                <li>
                  <NavItemLink to="/settings/invitations" label="Invitations" />
                </li>
              )}
            </ul>
          </NavGroup>
        )}
      </nav>
    </aside>
  );
}
