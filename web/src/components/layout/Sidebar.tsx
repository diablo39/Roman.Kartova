import { NavLink } from "react-router-dom";
import { Folder, Server, Book, Settings as SettingsIcon, Boxes } from "lucide-react";
import { cn } from "@/lib/utils";

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  enabled: boolean;
}

const NAV_ITEMS: NavItem[] = [
  { to: "/catalog", label: "Catalog", icon: Folder, enabled: true },
  { to: "/services", label: "Services", icon: Boxes, enabled: false },
  { to: "/infrastructure", label: "Infrastructure", icon: Server, enabled: false },
  { to: "/docs", label: "Docs", icon: Book, enabled: false },
  { to: "/settings", label: "Settings", icon: SettingsIcon, enabled: false },
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
                    cn(
                      "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                      isActive
                        ? "bg-primary text-primary-foreground"
                        : "text-foreground hover:bg-card-elevated",
                    )
                  }
                >
                  <item.icon className="h-4 w-4" />
                  {item.label}
                </NavLink>
              ) : (
                <span
                  className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-muted opacity-50"
                  data-disabled="true"
                >
                  <item.icon className="h-4 w-4" />
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
