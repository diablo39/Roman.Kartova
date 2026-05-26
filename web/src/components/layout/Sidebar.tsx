import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

interface NavItem {
  to: string;
  label: string;
  enabled: boolean;
}

export function Sidebar() {
  const { hasPermission } = usePermissions();
  const canSeeTeams = hasPermission(KartovaPermissions.TeamRead);

  const items: NavItem[] = [
    { to: "/catalog", label: "Catalog", enabled: true },
    ...(canSeeTeams ? [{ to: "/teams", label: "Teams", enabled: true }] : []),
    { to: "/services", label: "Services", enabled: false },
    { to: "/infrastructure", label: "Infrastructure", enabled: false },
    { to: "/docs", label: "Docs", enabled: false },
    { to: "/settings", label: "Settings", enabled: false },
  ];

  return (
    <aside className="flex h-full w-[260px] flex-col border-r border-secondary bg-secondary">
      <div className="flex h-14 items-center border-b border-secondary px-4">
        <span className="text-lg font-semibold text-primary">Kartova</span>
      </div>
      <nav className="flex-1 overflow-y-auto p-3">
        <ul className="space-y-1">
          {items.map(item => (
            <li key={item.to}>
              {item.enabled ? (
                <NavLink
                  to={item.to}
                  className={({ isActive }) =>
                    cx(
                      "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                      isActive
                        ? "bg-brand-solid text-white"
                        : "text-secondary hover:bg-primary_hover",
                    )
                  }
                >
                  {item.label}
                </NavLink>
              ) : (
                <span
                  className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-tertiary opacity-50"
                  data-disabled="true"
                >
                  {item.label}
                </span>
              )}
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  );
}
