import { NavLink } from "react-router-dom";
import { cx } from "@/lib/utils/cx";

interface NavItem {
  to: string;
  label: string;
  enabled: boolean;
}

const NAV_ITEMS: NavItem[] = [
  { to: "/catalog", label: "Catalog", enabled: true },
  { to: "/services", label: "Services", enabled: false },
  { to: "/infrastructure", label: "Infrastructure", enabled: false },
  { to: "/docs", label: "Docs", enabled: false },
  { to: "/settings", label: "Settings", enabled: false },
];

export function Sidebar() {
  return (
    <aside className="flex h-full w-[260px] flex-col border-r border-border bg-card">
      <div className="flex h-14 items-center border-b border-border px-4">
        <span className="text-lg font-semibold">Kartova</span>
      </div>
      <nav className="flex-1 overflow-y-auto p-3">
        <ul className="space-y-1">
          {NAV_ITEMS.map(item => (
            <li key={item.to}>
              {item.enabled ? (
                <NavLink
                  to={item.to}
                  className={({ isActive }) =>
                    cx(
                      "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                      isActive
                        ? "bg-primary text-primary-foreground"
                        : "text-foreground hover:bg-card-elevated",
                    )
                  }
                >
                  {item.label}
                </NavLink>
              ) : (
                <span
                  className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-muted opacity-50"
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
