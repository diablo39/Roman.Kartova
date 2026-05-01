import { useEffect } from "react";
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

function ApiAuthBridge({ children }: { children: React.ReactNode }) {
  const auth = useAuth();
  useEffect(() => {
    setAccessTokenProvider(() => auth.user?.access_token ?? null);
    setUnauthorizedHandler(() => {
      void auth.signinRedirect();
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
