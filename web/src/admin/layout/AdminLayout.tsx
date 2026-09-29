import { useEffect } from "react";
import { useLocation } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";
import { ShellLayout } from "@/components/layout/ShellLayout";
import { SidebarFrame } from "@/components/layout/SidebarFrame";
import { TopBarFrame } from "@/components/layout/TopBarFrame";
import { statusOf } from "@/shared/api/openapi-fetch-helpers";
import { useAdminSession } from "../api/useAdminSession";
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
 * already called signinRedirect) — or, if that redirect failed (auth.error from signinRedirect), a
 * "Sign-in unavailable" panel; anything else — 5xx or a network failure — is retryable, never "no access".
 */
export function AdminLayout() {
  const session = useAdminSession();
  const auth = useAuth();
  const { pathname, search, hash } = useLocation();
  const signOut = () => void auth.signoutRedirect();
  const status = session.isError ? statusOf(session.error) : undefined;

  // 401/403 are expected, handled outcomes (re-auth in progress / no access) — never logged as
  // failures. Everything else (5xx, network failure, a contract-violating body) is unexpected and
  // would otherwise fail silently behind the retry panel.
  useEffect(() => {
    if (session.isError && status !== 401 && status !== 403) {
      console.error("Admin access check failed:", session.error);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- status is derived from session.error; re-deriving it in the dep array would re-run on every session object identity change.
  }, [session.isError, session.error]);

  if (session.isPending) return <div className="p-8 text-sm text-tertiary">Loading…</div>;
  if (session.isError) {
    if (status === 403) return <AdminNoAccessPage onSignOut={signOut} />;
    if (status === 401) {
      if (auth.error?.source === "signinRedirect") {
        return (
          <CenteredMessage
            heading="Sign-in unavailable"
            body="The sign-in service could not be reached."
            action={
              <Button
                color="secondary"
                size="md"
                onClick={() => void auth.signinRedirect({ state: { returnTo: pathname + search + hash } })}
              >
                Try again
              </Button>
            }
          />
        );
      }
      return <div className="p-8 text-sm text-tertiary">Signing in…</div>;
    }
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
          user={{ displayName: session.data.displayName, email: session.data.email }}
          onSignOut={signOut}
        />
      }
    />
  );
}
