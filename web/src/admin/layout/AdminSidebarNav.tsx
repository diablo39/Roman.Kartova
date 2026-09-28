import { DisabledItem, NavItemLink } from "@/components/layout/sidebar-nav";

/** Admin console navigation. Organizations lights up with E-01b.F-01. */
export function AdminSidebarNav() {
  return (
    <ul className="space-y-1">
      <li>
        <NavItemLink to="/" label="Overview" end />
      </li>
      <li>
        <DisabledItem label="Organizations" hint="Coming soon" />
      </li>
    </ul>
  );
}
