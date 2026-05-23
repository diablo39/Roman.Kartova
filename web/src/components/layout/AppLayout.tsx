import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { NoAccessPage } from "./NoAccessPage";

function SkeletonShell() {
  return <div className="p-8 text-sm text-tertiary">Loading…</div>;
}

function PermissionsErrorShell() {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="max-w-md space-y-3 text-center">
        <h1 className="text-2xl font-semibold text-primary">Couldn't load your permissions</h1>
        <p className="text-sm text-tertiary">
          Please refresh the page. If the problem persists, contact support.
        </p>
      </div>
    </div>
  );
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
  const { hasPermission, isLoading, isError } = usePermissions();
  if (isLoading) return <SkeletonShell />;
  if (isError) return <PermissionsErrorShell />;
  if (!hasPermission(KartovaPermissions.CatalogRead)) return <NoAccessPage />;
  return <ProtectedShell />;
}
