import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { NoAccessPage } from "./NoAccessPage";
import { ShellLayout } from "./ShellLayout";

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
  return <ShellLayout sidebar={<Sidebar />} topBar={<TopBar />} />;
}

export function AppLayout() {
  const { hasPermission, isLoading, isError } = usePermissions();
  if (isLoading) return <SkeletonShell />;
  if (isError) return <PermissionsErrorShell />;
  if (!hasPermission(KartovaPermissions.CatalogRead)) return <NoAccessPage />;
  return <ProtectedShell />;
}
