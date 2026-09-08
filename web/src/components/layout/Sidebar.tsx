import { useState } from "react";
import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

/**
 * Visual section header inside the sidebar. Renders a small uppercase title
 * above a stack of `NavItemLink`s — used by Slice-9 F7 to group the
 * permission-gated "Settings" sub-navigation under a dedicated heading so it
 * reads as a distinct section instead of a fourth top-level link.
 */
function NavGroup({
  title,
  children,
  className = "mt-4",
}: {
  title: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("space-y-1", className)} data-testid={`nav-group-${title.toLowerCase()}`}>
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
 * Extracted so the Catalog and Settings groups can render sub-items with
 * identical chrome.
 *
 * Plain `NavLink` descendant matching is correct for every item: Applications
 * (`/catalog/applications`), Services (`/catalog/services`), APIs
 * (`/catalog/apis`), and Systems (`/catalog/systems`) no longer share a path
 * prefix beyond `/catalog`, so none highlights on another's routes, while each
 * still lights up on its own detail pages (`…/:id`).
 */
function NavItemLink({ to, label }: { to: string; label: string }) {
  return (
    <NavLink to={to} className={({ isActive }) => navItemClass(isActive)}>
      {label}
    </NavLink>
  );
}

/**
 * Collapsible sub-group inside a NavGroup — a clickable header (with a rotating
 * chevron) that shows/hides a stack of nav items. Used to split the Catalog
 * section into "Software" and "Infrastructure". Open/closed state is remembered
 * per browser session under `nav.group.<storageKey>` (same sessionStorage
 * convention as the Hierarchy tree); reads are guarded because storage access
 * can throw in locked-down contexts.
 */
function NavCollapsibleGroup({
  title,
  storageKey,
  children,
}: {
  title: string;
  storageKey: string;
  children: React.ReactNode;
}) {
  const key = `nav.group.${storageKey}`;
  const [open, setOpen] = useState<boolean>(() => {
    try {
      return sessionStorage.getItem(key) !== "false"; // default open
    } catch {
      return true;
    }
  });

  const toggle = () => {
    const next = !open;
    setOpen(next);
    try {
      sessionStorage.setItem(key, String(next));
    } catch {
      /* storage unavailable — keep in-memory state only */
    }
  };

  return (
    <div className="space-y-1" data-testid={`nav-collapsible-${storageKey}`}>
      <button
        type="button"
        onClick={toggle}
        aria-expanded={open}
        className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm font-medium text-primary transition-colors hover:bg-primary_hover"
      >
        <span>{title}</span>
        <svg
          className={cx("ml-auto size-4 shrink-0 transition-transform motion-reduce:transition-none", !open && "-rotate-90")}
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.2"
          aria-hidden="true"
        >
          <path d="M6 9l6 6 6-6" />
        </svg>
      </button>
      {open && (
        <ul className="space-y-1 pl-2">{children}</ul>
      )}
    </div>
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
        <NavGroup title="Catalog" className="mt-0">
          <div className="space-y-1">
            <NavCollapsibleGroup title="Software" storageKey="software">
              <li>
                <NavItemLink to="/catalog/applications" label="Applications" />
              </li>
              <li>
                <NavItemLink to="/catalog/services" label="Services" />
              </li>
              <li>
                <NavItemLink to="/catalog/apis" label="APIs" />
              </li>
              <li>
                <NavItemLink to="/catalog/systems" label="Systems" />
              </li>
            </NavCollapsibleGroup>
            <NavCollapsibleGroup title="Infrastructure" storageKey="infrastructure">
              <li>
                <DisabledItem label="Components" />
              </li>
              <li>
                <DisabledItem label="Brokers" />
              </li>
            </NavCollapsibleGroup>
            <ul className="space-y-1">
              <li>
                <NavItemLink to="/catalog/hierarchy" label="Hierarchy" />
              </li>
              <li>
                <DisabledItem label="Docs" />
              </li>
            </ul>
          </div>
        </NavGroup>
        {(canSeeTeams || canSeeMembers) && (
          <ul className="mt-4 space-y-1">
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
          </ul>
        )}
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
