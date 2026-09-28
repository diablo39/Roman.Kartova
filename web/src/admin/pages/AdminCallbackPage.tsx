import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";
import { resolveReturnTo } from "@/shared/oidc/returnTo";

/**
 * `/callback` — OIDC return URL of the admin console. No session bootstrap (POST /api/v1/auth/session is
 * tenant-only): once the code exchange completes, route to the validated deep link, else `/`.
 */
export function AdminCallbackPage() {
  const auth = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.error) console.error("OIDC callback failed:", auth.error);
  }, [auth.error]);

  useEffect(() => {
    if (!auth.error && !auth.isLoading && auth.isAuthenticated) {
      navigate(resolveReturnTo(auth.user?.state) ?? "/", { replace: true });
    }
  }, [auth.error, auth.isLoading, auth.isAuthenticated, auth.user, navigate]);

  if (auth.error) {
    return (
      <CenteredMessage
        heading="Sign-in failed"
        body="The sign-in could not be completed."
        action={
          <Button color="secondary" size="md" onClick={() => void auth.signinRedirect()}>
            Try again
          </Button>
        }
      />
    );
  }
  return <div className="p-8 text-sm text-tertiary">Completing sign-in…</div>;
}
