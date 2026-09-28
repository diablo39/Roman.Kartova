import { useEffect } from "react";
import { useAuth } from "react-oidc-context";

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
      // Round-trip the originally-requested URL (path + query + hash) through
      // the OIDC `state` so the post-login callback can restore the deep link
      // instead of dumping every user on /catalog (and dropping filter params).
      void auth.signinRedirect({
        state: {
          returnTo:
            window.location.pathname + window.location.search + window.location.hash,
        },
      });
    }
  }, [auth]);

  if (auth.isLoading || !auth.isAuthenticated) {
    return <div className="p-8 text-sm text-muted-foreground">Signing in…</div>;
  }
  return <>{children}</>;
}
