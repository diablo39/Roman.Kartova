import { useAuth } from "react-oidc-context";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";
import { ShellLayout } from "@/components/layout/ShellLayout";
import { SidebarFrame } from "@/components/layout/SidebarFrame";
import { TopBarFrame } from "@/components/layout/TopBarFrame";
import { statusOf } from "@/shared/api/openapi-fetch-helpers";
import { useAdminSession } from "../api/useAdminSession";
import { orDash } from "../format";
import { AdminNoAccessPage } from "../pages/AdminNoAccessPage";
import { AdminSidebarNav } from "./AdminSidebarNav";

function SessionErrorPanel({ onRetry }: { onRetry: () => void }) {
  return (
    <CenteredMessage
      heading="Couldn't verify admin access"
      body="The access check failed. Try again; if it keeps failing, check the API."
      action={
        <Button color="secondary" size="md" onClick={onRetry}>
          Retry
        </Button>
      }
    />
  );
}

/**
 * Access gate for the whole console (ADR-0118): GET /api/v1/admin/session/me decides.
 * 403 is the only "no access" answer; 401 is a re-auth in progress (the API client's 401 handler has
 * already called signinRedirect); anything else — 5xx or a network failure — is retryable, never "no access".
 */
export function AdminLayout() {
  const session = useAdminSession();
  const auth = useAuth();
  const signOut = () => void auth.signoutRedirect();

  if (session.isPending) return <div className="p-8 text-sm text-tertiary">Loading…</div>;
  if (session.isError) {
    const status = statusOf(session.error);
    if (status === 403) return <AdminNoAccessPage onSignOut={signOut} />;
    if (status === 401) return <div className="p-8 text-sm text-tertiary">Signing in…</div>;
    return <SessionErrorPanel onRetry={() => void session.refetch()} />;
  }

  return (
    <ShellLayout
      sidebar={
        <SidebarFrame>
          <AdminSidebarNav />
        </SidebarFrame>
      }
      topBar={
        <TopBarFrame
          identity={
            <Badge color="brand" type="pill-color" size="sm" className="uppercase tracking-wide">
              Platform Admin
            </Badge>
          }
          user={{ displayName: orDash(session.data.displayName), email: orDash(session.data.email) }}
          onSignOut={signOut}
        />
      }
    />
  );
}
