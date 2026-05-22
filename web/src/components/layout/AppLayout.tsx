import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { NoAccessPage } from "./NoAccessPage";

function SkeletonShell() {
  return <div className="p-8 text-sm text-tertiary">Loading…</div>;
}

function ProtectedShell() {
  return (
    <div className="flex h-full">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <TopBar />
        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export function AppLayout() {
  const { hasPermission, isLoading } = usePermissions();
  if (isLoading) return <SkeletonShell />;
  if (!hasPermission(KartovaPermissions.CatalogRead)) return <NoAccessPage />;
  return <ProtectedShell />;
}
