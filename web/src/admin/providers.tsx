import { useEffect, useRef } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider, useAuth } from "react-oidc-context";
import { ThemeProvider } from "next-themes";
import { buildOidcConfig } from "@/shared/oidc/authConfig";
import { setAdminAccessTokenProvider, setAdminUnauthorizedHandler } from "./api/client";

// ADR-0118: operator identities live in the kartova-platform realm; PKCE public client kartova-admin-web.
// Tokens sit in this origin's own sessionStorage — unreachable from the tenant SPA's origin.
const oidcConfig = buildOidcConfig({
  authority: import.meta.env.VITE_ADMIN_OIDC_AUTHORITY ?? "http://localhost:8180/realms/kartova-platform",
  clientId: import.meta.env.VITE_ADMIN_OIDC_CLIENT_ID ?? "kartova-admin-web",
  redirectUri: `${window.location.origin}/callback`,
  postLogoutRedirectUri: window.location.origin,
  storage: window.sessionStorage,
});

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false } },
});

/**
 * Mirrors the tenant ApiAuthBridge (web/src/app/providers.tsx, PR #47): refs are updated during render so
 * the first request after the loading→authenticated flip already carries the live token and the live
 * signinRedirect.
 */
export function AdminApiAuthBridge({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  const tokenRef = useRef<string | null>(null);
  // eslint-disable-next-line react-hooks/refs -- intentional: live refs must be set during render before child effects (PR #47).
  tokenRef.current = auth.user?.access_token ?? null;
  const signinRedirectRef = useRef(auth.signinRedirect);
  // eslint-disable-next-line react-hooks/refs -- intentional: live refs must be set during render before child effects (PR #47).
  signinRedirectRef.current = auth.signinRedirect;
  useEffect(() => {
    setAdminAccessTokenProvider(() => tokenRef.current);
    setAdminUnauthorizedHandler(() => {
      void signinRedirectRef.current({
        state: { returnTo: window.location.pathname + window.location.search + window.location.hash },
      });
    });
  }, [auth]);
  return <>{children}</>;
}

export function AdminProviders({ children }: { children: React.ReactNode }) {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem={true} value={{ dark: "dark-mode" }}>
      <AuthProvider {...oidcConfig}>
        <QueryClientProvider client={queryClient}>
          <AdminApiAuthBridge>{children}</AdminApiAuthBridge>
        </QueryClientProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
