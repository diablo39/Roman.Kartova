import { useState } from "react";
import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";

/**
 * Visual section header inside the sidebar. Renders a small uppercase title
 * above a stack of `NavItemLink`s — used by Slice-9 F7 to group the
 * permission-gated "Settings" sub-navigation under a dedicated heading so it
 * reads as a distinct section instead of a fourth top-level link.
 */
export function NavGroup({
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
 * Plain `NavLink` descendant matching is correct for most items: Applications
 * (`/catalog/applications`), Services (`/catalog/services`), APIs
 * (`/catalog/apis`), and Systems (`/catalog/systems`) no longer share a path
 * prefix beyond `/catalog`, so none highlights on another's routes, while each
 * still lights up on its own detail pages (`…/:id`).
 *
 * The Infrastructure group is the exception: "All Objects" (`/catalog/infrastructure`)
 * IS a path prefix of "Virtual Machines" (`/catalog/infrastructure/vms`), so with
 * descendant matching All Objects would also highlight on every VM route. Pass
 * `end` for such a parent link to require an exact-path match. (Virtual Machines
 * keeps descendant matching so it still lights up on `…/vms/:id`.)
 */
export function NavItemLink({ to, label, end }: { to: string; label: string; end?: boolean }) {
  return (
    <NavLink to={to} end={end} className={({ isActive }) => navItemClass(isActive)}>
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
export function NavCollapsibleGroup({
  title,
  storageKey,
  children,
}: {
  title: string;
  storageKey: string;
  children: React.ReactNode;
}) {
  const key = `nav.group.${storageKey}`;
  const listId = `nav-collapsible-${storageKey}-items`;
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
        aria-controls={listId}
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
      {/* Always rendered (hidden when collapsed) so `aria-controls` always
          resolves; the `hidden` subtree is excluded from the a11y tree, so
          collapsed items are unreachable by AT and by role queries. */}
      <ul id={listId} hidden={!open} className="space-y-1 pl-2">
        {children}
      </ul>
    </div>
  );
}

/**
 * Non-interactive placeholder for a not-yet-built nav destination. `hint` (e.g. "Coming soon") is shown
 * inline and as the native tooltip. Omitted → the element renders exactly as before (tenant sidebar).
 */
export function DisabledItem({ label, hint }: { label: string; hint?: string }) {
  return (
    <span
      className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-tertiary opacity-50"
      data-disabled="true"
      title={hint}
    >
      {label}
      {hint && <span className="ml-auto text-xs">{hint}</span>}
    </span>
  );
}
