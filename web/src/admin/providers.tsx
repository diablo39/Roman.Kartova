import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider } from "react-oidc-context";
import { ThemeProvider } from "next-themes";
import { buildOidcConfig } from "@/shared/oidc/authConfig";
import { useApiAuthBridge } from "@/shared/oidc/useApiAuthBridge";
import { setAdminAccessTokenProvider, setAdminUnauthorizedHandler } from "./api/client";
import { resolveConfigValue } from "@/shared/config/runtimeConfig";

// ADR-0118: operator identities live in the kartova-platform realm; PKCE public client kartova-admin-web.
// Tokens sit in this origin's own sessionStorage — unreachable from the tenant SPA's origin.
const oidcConfig = buildOidcConfig({
  authority: resolveConfigValue(
    "oidcAuthority",
    import.meta.env.VITE_ADMIN_OIDC_AUTHORITY,
    "http://localhost:8180/realms/kartova-platform",
  ),
  clientId: resolveConfigValue("oidcClientId", import.meta.env.VITE_ADMIN_OIDC_CLIENT_ID, "kartova-admin-web"),
  redirectUri: `${window.location.origin}/callback`,
  postLogoutRedirectUri: window.location.origin,
  storage: window.sessionStorage,
});

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false } },
});

export function AdminApiAuthBridge({ children }: { children: React.ReactNode }) {
  useApiAuthBridge(setAdminAccessTokenProvider, setAdminUnauthorizedHandler);
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
