import { useEffect, useRef } from "react";
import { useAuth } from "react-oidc-context";

/**
 * Shared live-ref bridge between react-oidc-context and an API client's token
 * provider / 401 handler slots. Used by both the tenant ApiAuthBridge
 * (web/src/app/providers.tsx) and the admin AdminApiAuthBridge
 * (web/src/admin/providers.tsx, PR #47 live-ref race fix) so the fix stays in
 * one place.
 *
 * Keep the latest access token AND signinRedirect in refs updated during
 * render. The bridge is the parent of the routed tree, so it renders before
 * any child query effect fires — the refs therefore hold live values by the
 * time the first authed request goes out. Capturing `auth` in the installed
 * closures instead (from an effect, which React runs child-first) let the
 * first request after the isLoading→authenticated flip race out with a stale
 * null token → 401, and would hand the 401 handler a stale signinRedirect
 * from the loading-phase render.
 */
export function useApiAuthBridge(
  setTokenProvider: (p: () => string | null) => void,
  setUnauthorizedHandler: (h: () => void) => void,
): void {
  const auth = useAuth();
  const tokenRef = useRef<string | null>(null);
  // eslint-disable-next-line react-hooks/refs -- intentional: live refs must be set during render before child effects (PR #47); effect-based assignment reintroduces the stale-token 401 race.
  tokenRef.current = auth.user?.access_token ?? null;
  const signinRedirectRef = useRef(auth.signinRedirect);
  // eslint-disable-next-line react-hooks/refs -- intentional: live refs must be set during render before child effects (PR #47); effect-based assignment reintroduces the stale-token 401 race.
  signinRedirectRef.current = auth.signinRedirect;
  // Re-entrancy guard: several concurrent 401s (e.g. a burst of in-flight requests racing the same
  // expired token) must not each fire their own signinRedirect — the browser would be handed
  // multiple overlapping navigations. Released when the redirect fails so a later 401 can retry.
  const redirectingRef = useRef(false);
  // react-oidc-context navigator methods never reject: a failed signinRedirect resolves null and
  // surfaces as auth.error with source "signinRedirect" (react-oidc-context 3.3.1). That is the
  // failure signal — release the guard and log, otherwise every later 401 is silently ignored.
  useEffect(() => {
    if (auth.error?.source === "signinRedirect") {
      redirectingRef.current = false;
      console.error("Re-authentication redirect failed:", auth.error);
    }
  }, [auth.error]);
  useEffect(() => {
    setTokenProvider(() => tokenRef.current);
    setUnauthorizedHandler(() => {
      if (redirectingRef.current) return;
      redirectingRef.current = true;
      // Round-trip the current deep link through OIDC `state` (mirrors
      // RequireAuth) so a 401-triggered re-auth returns the user to where they
      // were instead of dumping them on /catalog (resolveReturnTo validates it).
      void signinRedirectRef.current({
        state: {
          returnTo:
            window.location.pathname + window.location.search + window.location.hash,
        },
      });
    });
  }, [auth]);
}
