import { AuthProvider as OidcAuthProvider } from "react-oidc-context";
import { buildOidcConfig } from "./authConfig";

const DEFAULT_AUTHORITY = "http://localhost:8180/realms/kartova";
const DEFAULT_CLIENT_ID = "kartova-web";

const config = buildOidcConfig({
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? DEFAULT_AUTHORITY,
  clientId: import.meta.env.VITE_OIDC_CLIENT_ID ?? DEFAULT_CLIENT_ID,
  redirectUri: `${window.location.origin}/callback`,
});

export function AuthProvider({ children }: { children: React.ReactNode }) {
  return (
    <OidcAuthProvider
      {...config}
      onSigninCallback={() => {
        window.history.replaceState({}, document.title, "/");
      }}
    >
      {children}
    </OidcAuthProvider>
  );
}
