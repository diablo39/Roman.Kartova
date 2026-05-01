import { useEffect } from "react";
import { useAuth } from "react-oidc-context";

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const auth = useAuth();

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator) {
      void auth.signinRedirect();
    }
  }, [auth]);

  if (auth.isLoading || !auth.isAuthenticated) {
    return <div className="p-8 text-sm text-muted-foreground">Signing in…</div>;
  }
  return <>{children}</>;
}
