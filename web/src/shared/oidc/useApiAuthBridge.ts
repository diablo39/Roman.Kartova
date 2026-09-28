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
  // multiple overlapping navigations. Cleared on rejection so a genuinely failed redirect can retry.
  const redirectingRef = useRef(false);
  useEffect(() => {
    setTokenProvider(() => tokenRef.current);
    setUnauthorizedHandler(() => {
      if (redirectingRef.current) return;
      redirectingRef.current = true;
      // Round-trip the current deep link through OIDC `state` (mirrors
      // RequireAuth) so a 401-triggered re-auth returns the user to where they
      // were instead of dumping them on /catalog (resolveReturnTo validates it).
      // Promise.resolve(...) wraps the call so a mock/adapter that doesn't return a thenable
      // (any signinRedirect not typed strictly as Promise-returning) still lets .catch attach safely.
      Promise.resolve(
        signinRedirectRef.current({
          state: {
            returnTo:
              window.location.pathname + window.location.search + window.location.hash,
          },
        }),
      ).catch((e) => {
        redirectingRef.current = false;
        console.error("Re-authentication redirect failed:", e);
      });
    });
  }, [auth]);
}
