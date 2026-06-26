import { useEffect, useRef } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { ThemeProvider } from "next-themes";
import { Toaster } from "sonner";

import { AuthProvider } from "@/shared/auth/AuthProvider";
import {
  setAccessTokenProvider,
  setUnauthorizedHandler,
} from "@/features/catalog/api/client";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

export function ApiAuthBridge({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  // Keep the latest access token AND signinRedirect in refs updated during
  // render. ApiAuthBridge is the parent of the routed tree, so it renders
  // before any child query effect fires — the refs therefore hold live values
  // by the time the first authed request goes out. Capturing `auth` in the
  // installed closures instead (from an effect, which React runs child-first)
  // let the first request after the isLoading→authenticated flip race out with
  // a stale null token → 401, and would hand the 401 handler a stale
  // signinRedirect from the loading-phase render.
  const tokenRef = useRef<string | null>(null);
  tokenRef.current = auth.user?.access_token ?? null;
  const signinRedirectRef = useRef(auth.signinRedirect);
  signinRedirectRef.current = auth.signinRedirect;
  useEffect(() => {
    setAccessTokenProvider(() => tokenRef.current);
    setUnauthorizedHandler(() => {
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
  return <>{children}</>;
}

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem={true} value={{ dark: "dark-mode" }}>
      <AuthProvider>
        <QueryClientProvider client={queryClient}>
          <ApiAuthBridge>
            {children}
            <Toaster richColors position="top-right" />
          </ApiAuthBridge>
        </QueryClientProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
