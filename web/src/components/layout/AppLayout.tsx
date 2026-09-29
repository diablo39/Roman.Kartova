import { Sidebar } from "./Sidebar";
import { TopBar } from "./TopBar";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { CenteredMessage } from "./CenteredMessage";
import { NoAccessPage } from "./NoAccessPage";
import { ShellLayout } from "./ShellLayout";

function SkeletonShell() {
  return <div className="p-8 text-sm text-tertiary">Loading…</div>;
}

function PermissionsErrorShell() {
  return (
    <CenteredMessage
      heading="Couldn't load your permissions"
      body="Please refresh the page. If the problem persists, contact support."
    />
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
