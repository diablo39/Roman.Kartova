import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "react-oidc-context";

import { OidcCallbackHandler } from "@/features/auth/components/OidcCallbackHandler";
import { CenteredSpinner } from "@/features/auth/components/CenteredSpinner";
import { resolveReturnTo } from "@/shared/auth/returnTo";

/**
 * `/callback` — OIDC return URL. Has a two-phase lifecycle:
 *
 *   1. `react-oidc-context` first parses the URL fragment / query for the
 *      authorization code and exchanges it for an access token. While that
 *      is in flight `auth.isLoading` is true; if it fails, `auth.error` is
 *      populated.
 *   2. Once the token is in hand (`auth.isAuthenticated === true`) we hand
 *      off to `<OidcCallbackHandler />`, which runs the Kartova session
 *      bootstrap (`POST /api/v1/auth/session`) and routes onward to
 *      `/welcome`, `/catalog`, or `/login-error` (slice-9 spec §6).
 *
 * Auth errors short-circuit to `/login-error` so the user gets a real
 * recovery surface instead of being bounced back to `/` with a transient
 * toast (the slice-7 behaviour, retained until F6).
 *
 * The OIDC `state` round-trips the deep link `RequireAuth` stashed before the
 * login redirect; `resolveReturnTo` validates it (same-origin relative path,
 * not an auth route) and hands it to the handler so the user lands back where
 * they started instead of on `/catalog`.
 */
export function CallbackPage() {
  const auth = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (auth.error) {
      console.error("OIDC callback failed:", auth.error);
      navigate("/login-error", { replace: true });
    }
  }, [auth.error, navigate]);

  if (auth.error) {
    return <CenteredSpinner message="Sign-in failed; redirecting…" />;
  }
  if (auth.isLoading || !auth.isAuthenticated) {
    return <CenteredSpinner message="Completing sign-in…" />;
  }
  return <OidcCallbackHandler returnTo={resolveReturnTo(auth.user?.state)} />;
}
